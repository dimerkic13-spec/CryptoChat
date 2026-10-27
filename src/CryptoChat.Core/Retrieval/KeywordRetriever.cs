using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

/// <summary>Поиск по ключевым словам: область → оценка вопросов → топ-5.</summary>
public sealed class KeywordRetriever : IRetriever
{
    private const int MaxCandidates = 5;

    private readonly KnowledgeBase _kb;
    private readonly AppConfig _config;
    private readonly ScopeResolver _scopeResolver;

    public KeywordRetriever(KnowledgeBase kb, AppConfig config)
    {
        _kb = kb;
        _config = config;
        _scopeResolver = new ScopeResolver(kb, config);
    }

    public RetrievalResult Search(IReadOnlyList<string> lemmas, Category? forcedScope = null)
    {
        if (lemmas.Count == 0)
            return new RetrievalResult(lemmas, null, 0, Array.Empty<Candidate>());

        Category? scope;
        double scopeScore;
        if (forcedScope != null)
        {
            scope = forcedScope;
            scopeScore = 1.0;
        }
        else
        {
            var resolved = _scopeResolver.Resolve(lemmas);
            scope = resolved.Category;
            scopeScore = resolved.Score;
        }

        if (scope != null)
        {
            var inScope = Rank(_kb.QuestionsUnder(scope.Id), lemmas, _config.Scoring.ScopeBoost);
            if (inScope.Count > 0 || forcedScope != null)
                return new RetrievalResult(lemmas, scope, scopeScore, inScope);
            // В области ничего не нашлось: ищем по всей базе.
        }

        var everywhere = Rank(_kb.Questions, lemmas, 0);
        return new RetrievalResult(lemmas, null, scope == null ? scopeScore : 0, everywhere);
    }

    private List<Candidate> Rank(IEnumerable<Question> pool, IReadOnlyList<string> lemmas, double boost)
    {
        var scored = new List<(Question Question, double Score, int Matched)>();
        foreach (var question in pool.Where(q => q.IsActive))
        {
            var (score, matched) = Scorer.Evaluate(question.Keywords, lemmas, _config.Scoring.PrefixMinLength);
            if (score <= 0) continue;
            var final = boost > 0 ? Math.Min(1.0, score * (1 + boost)) : score;
            scored.Add((question, final, matched));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Matched)
            .ThenBy(x => x.Question.Id, IdComparer.Instance)
            .Take(MaxCandidates)
            .Select(x => new Candidate(x.Question, x.Score))
            .ToList();
    }
}
