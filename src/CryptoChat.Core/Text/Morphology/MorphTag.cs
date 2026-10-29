namespace CryptoChat.Core.Text.Morphology;

/// <summary>
/// Грамматическая информация о словоформе в формате OpenCorpora, например «NOUN,inan,femn sing,nomn».
/// Два тега равны, если у них одинаковые граммемы в одинаковом порядке (как в pymorphy3).
/// </summary>
internal sealed class MorphTag : IEquatable<MorphTag>
{
    public static readonly IReadOnlySet<string> PartsOfSpeech = new HashSet<string>
    {
        "NOUN", "ADJF", "ADJS", "COMP", "VERB", "INFN", "PRTF", "PRTS", "GRND",
        "NUMR", "ADVB", "NPRO", "PRED", "PREP", "CONJ", "PRCL", "INTJ"
    };

    private static readonly IReadOnlySet<string> NonProductive = new HashSet<string>
    {
        "NUMR", "NPRO", "PRED", "PREP", "CONJ", "PRCL", "INTJ", "Apro"
    };

    /// <summary>Граммемы, по которым сравниваются части слова через дефис: часть речи, число, падеж, лицо, время.</summary>
    public static readonly IReadOnlySet<string> FeatureGrammemes = new HashSet<string>(PartsOfSpeech
        .Concat(new[] { "sing", "plur" })
        .Concat(new[] { "nomn", "gent", "datv", "accs", "ablt", "loct", "voct", "gen1", "gen2", "acc2", "loc1", "loc2" })
        .Concat(new[] { "1per", "2per", "3per" })
        .Concat(new[] { "pres", "past", "futr" }));

    /// <summary>Граммемы через запятую: по нему теги сравниваются.</summary>
    public string Key { get; }

    /// <summary>Тег в том виде, как он записан в словаре (нужен для поиска вероятностей).</summary>
    public string Text { get; }

    public IReadOnlyList<string> GrammemeList { get; }
    public IReadOnlySet<string> Grammemes { get; }

    public MorphTag(string text)
    {
        Text = text;
        var space = text.IndexOf(' ');
        Key = space < 0 ? text : string.Concat(text.AsSpan(0, space), ",", text.AsSpan(space + 1));
        GrammemeList = Key.Split(',');
        Grammemes = new HashSet<string>(GrammemeList);
    }

    public string PartOfSpeech => GrammemeList[0];

    public bool IsProductive => !Grammemes.Overlaps(NonProductive);

    public bool IsUnknown => !PartsOfSpeech.Contains(PartOfSpeech);

    public bool Contains(params string[] grammemes) => grammemes.All(Grammemes.Contains);

    public bool Equals(MorphTag? other) => other != null && Key == other.Key;
    public override bool Equals(object? obj) => Equals(obj as MorphTag);
    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Text;
}
