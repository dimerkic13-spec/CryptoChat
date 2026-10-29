using System.Text;
using System.Text.Json;

namespace CryptoChat.Core.Text.Morphology;

/// <summary>
/// Словари pymorphy3 для русского языка (пакет pymorphy3-dicts-ru, данные OpenCorpora):
/// слова и их парадигмы (наборы окончаний), теги, суффиксы для предсказания незнакомых слов
/// и вероятности тегов P(t|w) для выбора самого частого разбора.
/// </summary>
internal sealed class MorphDictionary
{
    public const string FormatVersion = "2.4";

    public IReadOnlyList<MorphTag> Gramtab { get; }
    public IReadOnlyList<string> Suffixes { get; }
    public IReadOnlyList<ushort[]> Paradigms { get; }
    public IReadOnlyList<string> ParadigmPrefixes { get; }
    public RecordDawg Words { get; }
    public IReadOnlyList<RecordDawg> PredictionSuffixes { get; }
    public IntDawg? Probabilities { get; }
    public int MaxSuffixLength { get; }
    public string SourceRevision { get; }

    private MorphDictionary(IReadOnlyList<MorphTag> gramtab, IReadOnlyList<string> suffixes,
        IReadOnlyList<ushort[]> paradigms, IReadOnlyList<string> paradigmPrefixes, RecordDawg words,
        IReadOnlyList<RecordDawg> predictionSuffixes, IntDawg? probabilities, int maxSuffixLength,
        string sourceRevision)
    {
        Gramtab = gramtab;
        Suffixes = suffixes;
        Paradigms = paradigms;
        ParadigmPrefixes = paradigmPrefixes;
        Words = words;
        PredictionSuffixes = predictionSuffixes;
        Probabilities = probabilities;
        MaxSuffixLength = maxSuffixLength;
        SourceRevision = sourceRevision;
    }

    public static MorphDictionary Load(string path)
    {
        string F(string name) => Path.Combine(path, name);

        using var metaDocument = JsonDocument.Parse(File.ReadAllText(F("meta.json"), Encoding.UTF8));
        var meta = new Dictionary<string, JsonElement>();
        foreach (var pair in metaDocument.RootElement.EnumerateArray())
            meta[pair[0].GetString()!] = pair[1].Clone();

        var format = meta.TryGetValue("format_version", out var f) ? f.ToString() : "0.0";
        if (format.Split('.')[0] != FormatVersion.Split('.')[0])
            throw new InvalidDataException(
                $"Словари в {path} имеют формат «{format}», нужен формат «{FormatVersion}».");

        var options = meta.TryGetValue("compile_options", out var compile) ? compile : meta["prediction_options"];
        var maxSuffixLength = options.GetProperty("max_suffix_length").GetInt32();
        var paradigmPrefixes = options.TryGetProperty("paradigm_prefixes", out var prefixes)
            ? prefixes.EnumerateArray().Select(p => p.GetString()!).ToList()
            : ReadJson<List<string>>(F("paradigm-prefixes.json"));

        var gramtabFile = meta["gramtab_formats"].GetProperty("opencorpora-int").GetString()!;
        var gramtab = ReadJson<List<string>>(F(gramtabFile)).Select(t => new MorphTag(t)).ToList();
        var suffixes = ReadJson<List<string>>(F("suffixes.json"));

        var paradigms = new List<ushort[]>();
        using (var reader = new BinaryReader(File.OpenRead(F("paradigms.array"))))
        {
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadUInt16();
                var paradigm = new ushort[length];
                for (int j = 0; j < length; j++)
                    paradigm[j] = reader.ReadUInt16();
                paradigms.Add(paradigm);
            }
        }

        var words = RecordDawg.Load(F("words.dawg"), 2, 2);
        var prediction = Enumerable.Range(0, paradigmPrefixes.Count)
            .Select(i => RecordDawg.Load(F($"prediction-suffixes-{i}.dawg"), 4, 2, 2))
            .ToList();

        IntDawg? probabilities = null;
        if (meta.TryGetValue("P(t|w)", out var hasProbabilities) && hasProbabilities.ValueKind == JsonValueKind.True)
            probabilities = IntDawg.Load(F("p_t_given_w.intdawg"));

        var revision = meta.TryGetValue("source_revision", out var r) ? r.ToString() : "?";
        return new MorphDictionary(gramtab, suffixes, paradigms, paradigmPrefixes, words, prediction,
            probabilities, maxSuffixLength, revision);
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8))
        ?? throw new InvalidDataException($"Файл пуст: {path}");

    public MorphTag BuildTagInfo(int paraId, int idx)
    {
        var paradigm = Paradigms[paraId];
        return Gramtab[paradigm[paradigm.Length / 3 + idx]];
    }

    public string BuildNormalForm(int paraId, int idx, string fixedWord)
    {
        if (idx == 0) // начальная форма — само слово
            return fixedWord;

        var paradigm = Paradigms[paraId];
        var length = paradigm.Length / 3;
        var stem = BuildStem(paradigm, idx, fixedWord);
        var normalPrefix = ParadigmPrefixes[paradigm[length * 2]];
        var normalSuffix = Suffixes[paradigm[0]];
        return normalPrefix + stem + normalSuffix;
    }

    private string BuildStem(ushort[] paradigm, int idx, string fixedWord)
    {
        var length = paradigm.Length / 3;
        var prefix = ParadigmPrefixes[paradigm[length * 2 + idx]];
        var suffix = Suffixes[paradigm[idx]];
        return suffix.Length > 0
            ? PySlice(fixedWord, prefix.Length, fixedWord.Length - suffix.Length)
            : PySlice(fixedWord, prefix.Length, fixedWord.Length);
    }

    /// <summary>Срез строки как в Python: выход за границы даёт пустую строку, а не ошибку.</summary>
    internal static string PySlice(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return end > start ? text[start..end] : "";
    }

    /// <summary>Вероятность тега для слова P(t|w) или 0, если данных нет.</summary>
    public double Probability(string wordLower, MorphTag tag) =>
        Probabilities == null ? 0 : Probabilities.Get($"{wordLower}:{tag.Text}") / 1000000.0;
}
