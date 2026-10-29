using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Semantic;
using CryptoChat.Core.Text;

namespace CryptoChat.Core.Retrieval;

/// <summary>Поиск по ключевым словам: область → оценка вопросов → топ-5.</summary>
public sealed class KeywordRetriever : IRetriever
{
    private const int MaxCandidates = 5;

    private readonly KnowledgeBase _kb;
    private readonly AppConfig _config;
    private readonly ScopeResolver _scopeResolver;

    private static readonly IReadOnlyDictionary<string, double> None = new Dictionary<string, double>();

    private IReadOnlyDictionary<string, double> _cosines = None;        // похожести текущего запроса
    private IReadOnlyDictionary<string, double> _semanticScores = None; // и их перевод в оценки

    /// <summary>Учёт опечаток (null — выключен).</summary>
    public FuzzyMatcher? Fuzzy { get; }

    /// <summary>Поиск по смыслу (null — выключен).</summary>
    public SemanticIndex? Semantic { get; }

    /// <param name="fuzzy">Учёт опечаток. Если не передан, создаётся по config.json → fuzzy.</param>
    public KeywordRetriever(KnowledgeBase kb, AppConfig config, FuzzyMatcher? fuzzy = null, SemanticIndex? semantic = null)
    {
        _kb = kb;
        _config = config;
        Semantic = semantic;
        Fuzzy = fuzzy ?? FuzzyMatcher.FromConfig(config.Fuzzy);
        _scopeResolver = new ScopeResolver(kb, config, Fuzzy);
    }

    public RetrievalResult Search(IReadOnlyList<string> lemmas, Category? forcedScope = null, string? query = null)
    {
        if (lemmas.Count == 0)
            return new RetrievalResult(lemmas, null, 0, Array.Empty<Candidate>());

        _cosines = None;
        _semanticScores = None;
        if (Semantic != null && !string.IsNullOrEmpty(query))
        {
            _cosines = Semantic.Similarities(query);
            _semanticScores = Semantic.Scores(query);
        }

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
                return WithSemantic(new RetrievalResult(lemmas, scope, scopeScore, inScope));
            // В области ничего не нашлось: ищем по всей базе.
        }

        var everywhere = Rank(_kb.Questions, lemmas, 0);
        return WithSemantic(new RetrievalResult(lemmas, null, scope == null ? scopeScore : 0, everywhere));
    }

    private RetrievalResult WithSemantic(RetrievalResult result) =>
        result with
        {
            Semantic = new Dictionary<string, double>(_semanticScores),
            Cosines = new Dictionary<string, double>(_cosines)
        };

    private List<Candidate> Rank(IEnumerable<Question> pool, IReadOnlyList<string> lemmas, double boost)
    {
        var scored = new List<(Question Question, double Score, int Matched, double KeywordScore)>();
        foreach (var question in pool.Where(q => q.IsActive))
        {
            var (keywordScore, matched) = Scorer.Evaluate(
                question.Keywords, lemmas, _config.Scoring.PrefixMinLength, Fuzzy);
            var score = Scorer.CombineScores(keywordScore, _semanticScores.GetValueOrDefault(question.Id),
                _config.Semantic.Combine);
            if (score <= 0) continue;
            var final = boost > 0 ? Math.Min(1.0, score * (1 + boost)) : score;
            scored.Add((question, final, matched, keywordScore));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Matched)
            .ThenBy(x => x.Question.Id, IdComparer.Instance)
            .Take(MaxCandidates)
            .Select(x => new Candidate(x.Question, x.Score, x.KeywordScore,
                _cosines.TryGetValue(x.Question.Id, out var cosine) ? cosine : null))
            .ToList();
    }
}
