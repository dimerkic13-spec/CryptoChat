using CryptoChat.Core.Models;

namespace CryptoChat.Core.Text;

/// <summary>
/// Учёт опечаток: нечёткое сравнение слова запроса с ключевым словом.
/// «рутокин» ≈ «рутокен», «лицензя» ≈ «лицензия», «кабинед» ≈ «кабинет».
///
/// Похожесть от 0 до 100 считается так же, как fuzz.ratio в RapidFuzz (Python-версия):
/// 2 × (длина общей подпоследовательности) / (сумма длин) × 100.
///
/// Опечатка засчитывается, только если обе основы не короче MinLength букв
/// (короткие слова слишком легко спутать: «пин» и «пинг») и похожесть не ниже Threshold.
/// За совпадение с опечаткой ключ даёт не полный вес, а Weight от него,
/// чтобы точное совпадение всегда было весомее.
/// </summary>
public sealed class FuzzyMatcher
{
    private readonly Func<string, string, double> _ratio;
    private readonly Dictionary<(string, string), double> _cache = new();

    public double Threshold { get; }
    public int MinLength { get; }
    public double Weight { get; }
    public string Name { get; }

    public FuzzyMatcher(double threshold = 85, int minLength = 5, double weight = 0.8,
        Func<string, string, double>? ratio = null, string? name = null)
    {
        Threshold = threshold;
        MinLength = minLength;
        Weight = weight;
        _ratio = ratio ?? Ratio;
        Name = name ?? (ratio == null ? "встроенное сравнение (как RapidFuzz)" : "своя функция");
    }

    /// <summary>FuzzyMatcher по настройкам config.json → fuzzy, или null, если учёт опечаток выключен.</summary>
    public static FuzzyMatcher? FromConfig(FuzzyConfig config) =>
        config.Enabled ? new FuzzyMatcher(config.Threshold, config.MinLength, config.Weight) : null;

    /// <summary>
    /// Похожесть двух основ 0–100. Сравниваются и сами основы, и их укороченные стеммером формы:
    /// так «рутокина» (неизвестное словарю слово с опечаткой) всё равно похоже на «рутокен».
    /// </summary>
    public double Similarity(string a, string b)
    {
        if (_cache.TryGetValue((a, b), out var cached))
            return cached;
        var value = Math.Max(_ratio(a, b), _ratio(Stemmer.Stem(a), Stemmer.Stem(b)));
        _cache[(a, b)] = value;
        return value;
    }

    public bool Matches(string lemma, string keyword)
    {
        if (lemma.Length < MinLength || keyword.Length < MinLength)
            return false;
        return Similarity(lemma, keyword) >= Threshold;
    }

    /// <summary>Похожесть строк 0–100, как fuzz.ratio в RapidFuzz.</summary>
    public static double Ratio(string a, string b)
    {
        var total = a.Length + b.Length;
        if (total == 0)
            return 100;
        return 200.0 * LongestCommonSubsequence(a, b) / total;
    }

    private static int LongestCommonSubsequence(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
