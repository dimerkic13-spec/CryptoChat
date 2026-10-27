using CryptoChat.Core.Models;

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

    /// <summary>Score = вес совпавших ключей / вес всех ключей. Каждый ключ считается один раз.</summary>
    public static (double Score, int Matched) Evaluate(
        IReadOnlyList<Keyword> keywords, IReadOnlyList<string> lemmas, int prefixMinLength)
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
        }

        if (total <= 0)
            return (0.0, 0);
        return (matchedWeight / total, matched);
    }
}
