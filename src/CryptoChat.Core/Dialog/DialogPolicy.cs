using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;

namespace CryptoChat.Core.Dialog;

/// <summary>Решает по результату поиска: ответить, уточнить, спросить про продукт или передать в поддержку.</summary>
public sealed class DialogPolicy : IDialogPolicy
{
    private static readonly IReadOnlyList<Candidate> NoCandidates = Array.Empty<Candidate>();
    private static readonly IReadOnlyList<Category> NoProducts = Array.Empty<Category>();

    private readonly AppConfig _config;
    private readonly KnowledgeBase _kb;

    public DialogPolicy(AppConfig config, KnowledgeBase kb)
    {
        _config = config;
        _kb = kb;
    }

    public DialogDecision Decide(RetrievalResult result)
    {
        var t = _config.Thresholds;

        if (result.Lemmas.Count == 0)
            return new DialogDecision(DecisionKind.NotUnderstood, NoCandidates, NoProducts);

        var candidates = result.Candidates;
        if (candidates.Count == 0 || candidates[0].Score < t.Clarify)
            return new DialogDecision(DecisionKind.Escalate, NoCandidates, NoProducts);

        var top = candidates[0].Score;
        List<Candidate> options;

        if (top < t.Answer)
        {
            options = candidates.Where(c => c.Score >= t.Clarify).Take(_config.Menu.MaxOptions).ToList();
        }
        else
        {
            var strong = candidates.Where(c => c.Score >= t.Answer).ToList();
            var second = candidates.Count > 1 ? candidates[1].Score : 0.0;
            if (strong.Count == 1 || top - second >= t.DominanceGap)
                return new DialogDecision(DecisionKind.Answer, new[] { candidates[0] }, NoProducts);

            options = strong.Take(_config.Menu.MaxOptions).ToList();
        }

        if (result.Scope == null)
        {
            var products = options
                .Select(o => ProductOf(o.Question))
                .OfType<Category>()
                .DistinctBy(p => p.Id)
                .ToList();
            if (products.Count >= 2)
                return new DialogDecision(DecisionKind.ClarifyProduct, options, products);
        }

        return new DialogDecision(DecisionKind.Clarify, options, NoProducts);
    }

    private Category? ProductOf(Question question) =>
        _kb.NearestAncestorOrSelf(question.CategoryId, c => c.Kind == "product");
}
