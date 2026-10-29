using CryptoChat.Core.Models;
using CryptoChat.Core.Text;

namespace CryptoChat.Core.Retrieval;

public static class Scorer
{
    /// <summary>
    /// Основы совпадают, если равны. Для длинных основ (от prefixMinLength символов)
    /// достаточно, чтобы одна начиналась с другой.
    /// </summary>
    public static bool Match(string lemma, string keyword, int prefixMinLength) =>
        lemma == keyword
        || (Math.Min(lemma.Length, keyword.Length) >= prefixMinLength
            && (lemma.StartsWith(keyword, StringComparison.Ordinal)
                || keyword.StartsWith(lemma, StringComparison.Ordinal)));

    /// <summary>
    /// Score = вес совпавших ключей / вес всех ключей. Каждый ключ считается один раз.
    /// Ключ, совпавший только с опечаткой (fuzzy), даёт fuzzy.Weight от своего веса.
    /// </summary>
    public static (double Score, int Matched) Evaluate(
        IReadOnlyList<Keyword> keywords, IReadOnlyList<string> lemmas, int prefixMinLength,
        FuzzyMatcher? fuzzy = null)
    {
        double total = 0;
        double matchedWeight = 0;
        int matched = 0;

        foreach (var keyword in keywords)
        {
            total += keyword.Weight;
            if (lemmas.Any(l => Match(l, keyword.Word, prefixMinLength)))
            {
                matchedWeight += keyword.Weight;
                matched++;
            }
            else if (fuzzy != null && lemmas.Any(l => fuzzy.Matches(l, keyword.Word)))
            {
                matchedWeight += keyword.Weight * fuzzy.Weight;
                matched++;
            }
        }

        if (total <= 0)
            return (0.0, 0);
        return (matchedWeight / total, matched);
    }

    /// <summary>Совмещение оценок: "max" — лучшая из двух; "or" — обе усиливают друг друга (не больше 1).</summary>
    public static double CombineScores(double keyword, double semantic, string mode = "max") =>
        mode == "or" ? keyword + semantic - keyword * semantic : Math.Max(keyword, semantic);

    /// <summary>Пары (слово запроса, ключ), совпавшие только с учётом опечатки. Для отладки.</summary>
    public static IReadOnlyList<(string Lemma, string Keyword)> TypoMatches(
        IReadOnlyList<Keyword> keywords, IReadOnlyList<string> lemmas, int prefixMinLength, FuzzyMatcher? fuzzy)
    {
        var pairs = new List<(string, string)>();
        if (fuzzy == null)
            return pairs;

        foreach (var keyword in keywords)
        {
            if (lemmas.Any(l => Match(l, keyword.Word, prefixMinLength)))
                continue;
            var typo = lemmas.FirstOrDefault(l => fuzzy.Matches(l, keyword.Word));
            if (typo != null)
                pairs.Add((typo, keyword.Word));
        }
        return pairs;
    }
}
