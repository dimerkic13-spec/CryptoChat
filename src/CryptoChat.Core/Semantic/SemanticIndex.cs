using CryptoChat.Core.Models;

namespace CryptoChat.Core.Semantic;

/// <summary>То, что умеет превращать список фраз в список векторов. В тестах подменяется.</summary>
public interface IEncoder
{
    string Name { get; }
    IReadOnlyList<double[]> Encode(IReadOnlyList<string> texts);
}

/// <summary>Поиск по смыслу недоступен: нет модели или она не загрузилась.</summary>
public sealed class SemanticUnavailableException : Exception
{
    public SemanticUnavailableException(string message) : base(message) { }
    public SemanticUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Поиск по смыслу: векторы вопросов и расчёт смысловой оценки для запроса.
///
/// Модель превращает фразу в вектор чисел так, что близкие по смыслу фразы дают близкие векторы.
/// Поэтому «не могу зайти в аккаунт» находит «Забыл логин/пароль от личного кабинета»,
/// хотя общих слов у них нет. Текст ответа по-прежнему берётся дословно из базы:
/// модель только помогает выбрать вопрос.
///
///   • при запуске модель один раз считает векторы для заголовков вопросов и примеров из "examples";
///   • для запроса считается похожесть (косинус) с каждым вопросом;
///   • похожесть переводится в оценку 0–weight (semantic.mode):
///       "relative" — насколько вопрос выделяется на фоне остальных: z-оценка =
///         (похожесть − средняя) / разброс; учитываются только topK самых похожих вопросов;
///         ниже zLow — 0, выше zHigh — weight, между ними — линейно;
///       "absolute" — по самой похожести: ниже low — 0, выше high — weight.
/// Расчёты повторяют Python-версию вплоть до порядка сложения чисел.
/// </summary>
public class SemanticIndex
{
    private readonly IEncoder _encoder;
    private readonly List<string> _owners = new(); // для каждой фразы — id вопроса
    private readonly List<double[]> _vectors = new();
    private (string Query, Dictionary<string, double> Result) _last = ("", new());

    public string Name { get; }
    public double Low { get; }
    public double High { get; }
    public double Weight { get; }
    public string Mode { get; }
    public double ZLow { get; }
    public double ZHigh { get; }
    public int TopK { get; }
    public string QueryPrefix { get; }
    public string PassagePrefix { get; }

    public int Phrases => _vectors.Count;

    public SemanticIndex(IEncoder encoder, IEnumerable<Question> questions, double low = 0.55, double high = 0.9,
        double weight = 0.8, string? name = null, string mode = "absolute", double zLow = 2.5, double zHigh = 4.5,
        int topK = 3, string queryPrefix = "", string passagePrefix = "")
    {
        _encoder = encoder;
        Low = low;
        High = high;
        Weight = weight;
        Mode = mode;
        ZLow = zLow;
        ZHigh = zHigh;
        TopK = topK;
        QueryPrefix = queryPrefix;
        PassagePrefix = passagePrefix;
        Name = name ?? encoder.Name;

        var texts = new List<string>();
        foreach (var q in questions)
        {
            if (!q.IsActive)
                continue;
            foreach (var text in new[] { q.Title }.Concat(q.Examples))
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                texts.Add(passagePrefix + text.Trim());
                _owners.Add(q.Id);
            }
        }
        if (texts.Count > 0)
            _vectors.AddRange(encoder.Encode(texts).Select(Normalize));
    }

    /// <summary>
    /// Косинус запроса с каждым вопросом (лучшая из его фраз), в порядке вопросов в базе.
    /// Результат последнего запроса запоминается.
    /// </summary>
    public virtual IReadOnlyDictionary<string, double> Similarities(string? query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0 || _vectors.Count == 0)
            return new Dictionary<string, double>();
        if (_last.Query == query)
            return _last.Result;

        var q = Normalize(_encoder.Encode(new[] { QueryPrefix + query })[0]);
        var best = new Dictionary<string, double>();
        for (int i = 0; i < _vectors.Count; i++)
        {
            var value = Dot(q, _vectors[i]);
            if (value > best.GetValueOrDefault(_owners[i], -1.0))
                best[_owners[i]] = value;
        }
        _last = (query, best);
        return best;
    }

    /// <summary>Косинус → оценка 0–1 с учётом веса смыслового поиска.</summary>
    public double ToScore(double cosine) => Ramp(cosine, Low, High);

    /// <summary>Насколько каждый вопрос выделяется: (похожесть − средняя) / стандартное отклонение.</summary>
    public static Dictionary<string, double> ZScores(IReadOnlyDictionary<string, double> similarities)
    {
        var values = similarities.Values.ToList();
        if (values.Count < 2)
            return similarities.Keys.ToDictionary(k => k, _ => 0.0);
        var mean = PySum(values) / values.Count;
        var std = Math.Sqrt(PySum(values.Select(v => (v - mean) * (v - mean))) / values.Count);
        if (std <= 1e-9)
            return similarities.Keys.ToDictionary(k => k, _ => 0.0);
        return similarities.ToDictionary(x => x.Key, x => (x.Value - mean) / std);
    }

    private double Ramp(double value, double low, double high)
    {
        if (value <= low)
            return 0.0;
        if (value >= high)
            return Weight;
        return Weight * (value - low) / (high - low);
    }

    /// <summary>Смысловая оценка (уже с весом) для каждого вопроса, у которого она больше нуля.</summary>
    public IReadOnlyDictionary<string, double> Scores(string? query)
    {
        var sims = Similarities(query);
        IEnumerable<(string Id, double Score)> result;
        if (Mode == "relative")
        {
            var z = ZScores(sims);
            result = sims.OrderByDescending(x => x.Value).Take(TopK)
                .Select(x => (x.Key, Ramp(z[x.Key], ZLow, ZHigh)));
        }
        else
        {
            result = sims.Select(x => (x.Key, ToScore(x.Value)));
        }
        return result.Where(x => x.Score > 0).ToDictionary(x => x.Id, x => x.Score);
    }

    private static double[] Normalize(double[] vector)
    {
        var length = Math.Sqrt(PySum(vector.Select(x => x * x)));
        return length > 0 ? vector.Select(x => x / length).ToArray() : vector.ToArray();
    }

    private static double Dot(double[] a, double[] b) =>
        PySum(a.Zip(b, (x, y) => x * y));

    /// <summary>
    /// Сумма как у sum() в Python 3.12+: с компенсацией ошибок округления (алгоритм Ноймайера).
    /// Так оценки совпадают с Python-версией до последнего знака.
    /// </summary>
    public static double PySum(IEnumerable<double> values)
    {
        double sum = 0.0, compensation = 0.0;
        foreach (var x in values)
        {
            var t = sum + x;
            if (Math.Abs(sum) >= Math.Abs(x))
                compensation += (sum - t) + x;
            else
                compensation += (x - t) + sum;
            sum = t;
        }
        if (compensation != 0 && double.IsFinite(compensation))
            sum += compensation;
        return sum;
    }
}
