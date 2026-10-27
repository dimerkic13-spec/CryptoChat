using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

public sealed record ScopeResult(Category? Category, double Score);

/// <summary>
/// Определяет область поиска по ключевым словам категорий. Если уверенного лидера нет,
/// область не определена и поиск идёт по всей базе.
/// </summary>
public sealed class ScopeResolver
{
    private readonly AppConfig _config;
    private readonly List<Category> _categoriesWithKeywords;

    public ScopeResolver(KnowledgeBase knowledgeBase, AppConfig config)
    {
        _config = config;
        _categoriesWithKeywords = knowledgeBase.Categories.Values.Where(c => c.Keywords.Count > 0).ToList();
    }

    public ScopeResult Resolve(IReadOnlyList<string> lemmas)
    {
        if (_categoriesWithKeywords.Count == 0 || lemmas.Count == 0)
            return new ScopeResult(null, 0);

        var scored = _categoriesWithKeywords
            .Select(c => (Cat: c, Score: Scorer.Evaluate(c.Keywords, lemmas, _config.Scoring.PrefixMinLength).Score))
            .ToList();

        var best = scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => Depth(x.Cat.Id))
            .ThenBy(x => x.Cat.Id, IdComparer.Instance)
            .First();

        if (best.Score <= 0 || best.Score < _config.Thresholds.ScopeMin)
            return new ScopeResult(null, best.Score);

        var rival = scored
            .Where(x => !SameBranch(x.Cat.Id, best.Cat.Id))
            .Select(x => x.Score)
            .DefaultIfEmpty(0)
            .Max();

        return best.Score - rival >= _config.Thresholds.ScopeMargin
            ? new ScopeResult(best.Cat, best.Score)
            : new ScopeResult(null, best.Score);
    }

    private static int Depth(string id) => id.Count(c => c == '.');

    private static bool SameBranch(string a, string b) =>
        a == b
        || a.StartsWith(b + ".", StringComparison.Ordinal)
        || b.StartsWith(a + ".", StringComparison.Ordinal);
}
