using System.Text.RegularExpressions;

namespace CryptoChat.Core.Text;

public interface ITextProcessor
{
    /// <summary>Превращает текст в список основ слов (лемм).</summary>
    IReadOnlyList<string> Lemmatize(string text);
}

/// <summary>
/// Конвейер: нормализация → синонимы-фразы → слова → стоп-слова → стемминг → синонимы-слова.
/// </summary>
public sealed class TextProcessor : ITextProcessor
{
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}\.]+", RegexOptions.Compiled);

    public static readonly string[] DefaultStopWords =
    {
        "как", "что", "где", "какое", "для", "мне", "можно", "нужно", "ли",
        "вы", "вас", "ваш", "меня", "у", "я", "не", "в", "на", "с", "по",
        "это", "если", "или", "а", "но", "же", "бы", "чтобы", "хочу", "хотел",
        "вижу", "видеть", "видел", "есть", "был", "была", "было", "были",
        "такое", "какие", "какой", "какая", "такой", "при", "из", "от", "до",
        "за", "к", "о", "об", "то", "так"
    };

    private readonly HashSet<string> _stopWords;
    private readonly SynonymMap _synonyms;

    public TextProcessor(IEnumerable<string>? stopWords = null, SynonymMap? synonyms = null)
    {
        _stopWords = new HashSet<string>((stopWords ?? DefaultStopWords).Select(Normalizer.Normalize));
        _synonyms = synonyms ?? SynonymMap.Empty;
    }

    public IReadOnlyList<string> Lemmatize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var normalized = _synonyms.ReplacePhrases(Normalizer.Normalize(text));
        var lemmas = new List<string>();

        foreach (Match match in TokenRegex.Matches(normalized))
        {
            var token = match.Value.Trim('.');
            if (token.Length < 2 || _stopWords.Contains(token))
                continue;

            var lemma = Stemmer.Stem(token);
            if (lemma.Length == 0)
                continue;

            lemmas.Add(_synonyms.MapLemma(lemma));
        }

        return lemmas;
    }
}
