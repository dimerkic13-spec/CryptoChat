using System.Text.RegularExpressions;

namespace CryptoChat.Core.Text;

/// <summary>
/// Группы синонимов. Первое слово группы — каноническое (одно слово).
/// Остальные члены заменяются на него: одиночные слова — на уровне основ,
/// фразы из нескольких слов — в тексте до разбиения на слова.
/// </summary>
public sealed class SynonymMap
{
    private readonly List<(Regex Pattern, string Canonical)> _phrases = new();
    private readonly Dictionary<string, string> _lemmaMap = new();

    public static SynonymMap Empty { get; } = new(Array.Empty<IReadOnlyList<string>>());

    public SynonymMap(IEnumerable<IReadOnlyList<string>> groups)
    {
        foreach (var group in groups)
        {
            if (group.Count < 2)
                throw new ArgumentException("Группа синонимов должна содержать минимум два слова.");

            var canonical = Normalizer.Normalize(group[0]).Trim();
            if (canonical.Contains(' '))
                throw new ArgumentException($"Каноническое слово группы должно быть одним словом: «{canonical}».");
            var canonicalLemma = Stemmer.Stem(canonical);

            for (int i = 1; i < group.Count; i++)
            {
                var member = Normalizer.Normalize(group[i]).Trim();
                if (member.Contains(' '))
                {
                    var pattern = new Regex(
                        @"(?<![\p{L}\p{N}])" + Regex.Escape(member) + @"(?![\p{L}\p{N}])",
                        RegexOptions.Compiled);
                    _phrases.Add((pattern, canonical));
                }
                else
                {
                    var memberLemma = Stemmer.Stem(member);
                    if (!_lemmaMap.TryAdd(memberLemma, canonicalLemma))
                        throw new InvalidOperationException($"Слово «{member}» входит в несколько групп синонимов.");
                }
            }
        }
    }

    /// <summary>Заменяет фразы-синонимы в уже нормализованном тексте.</summary>
    public string ReplacePhrases(string normalizedText)
    {
        foreach (var (pattern, canonical) in _phrases)
            normalizedText = pattern.Replace(normalizedText, canonical);
        return normalizedText;
    }

    /// <summary>Заменяет основу слова на каноническую, если она входит в группу.</summary>
    public string MapLemma(string lemma) =>
        _lemmaMap.TryGetValue(lemma, out var canonical) ? canonical : lemma;
}
