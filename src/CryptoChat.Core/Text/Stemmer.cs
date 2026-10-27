using System.Text.RegularExpressions;

namespace CryptoChat.Core.Text;

/// <summary>
/// Грубый стеммер: отрезает окончания русских слов.
/// Один и тот же код применяется и к запросу, и к ключевым словам базы.
/// </summary>
public static class Stemmer
{
    private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex[] Rules =
    {
        new("(ал|ала|ало|али)$", Opt),
        new("(ать|ять|ить|еть|уть|ыть)$", Opt),
        new("(ть|ти)$", Opt),
        new("(ами|ями|ого|ему|ому|ыми|ими|ая|яя|ое|ее|ые|ие|ый|ий|ой|ей|ую|юю|ах|ях|ов|ев|ам|ям|ом|ем)$", Opt),
        new("(а|я|ы|и|у|ю|е|о)$", Opt),
    };

    public static string Stem(string word)
    {
        word = word.ToLowerInvariant();
        foreach (var rule in Rules)
            word = rule.Replace(word, "");
        return word;
    }
}
