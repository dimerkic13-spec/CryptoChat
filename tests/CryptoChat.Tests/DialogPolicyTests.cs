using CryptoChat.Core.Dialog;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using Xunit;

namespace CryptoChat.Tests;

public class DialogPolicyTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Build(TestSupport.TwoProducts());
    private static readonly DialogPolicy Policy = new(new AppConfig(), Kb);
    private static readonly string[] SomeLemmas = { "x" };

    private static Candidate C(int questionIndex, double score) => new(Kb.Questions[questionIndex], score);

    private static RetrievalResult R(Category? scope, params Candidate[] candidates) =>
        new(SomeLemmas, scope, 0, candidates);

    private static Category Scope => Kb.Categories["2.1"];

    [Fact]
    public void NoLemmas_IsNotUnderstood()
    {
        var decision = Policy.Decide(new RetrievalResult(Array.Empty<string>(), null, 0, Array.Empty<Candidate>()));
        Assert.Equal(DecisionKind.NotUnderstood, decision.Kind);
    }

    [Fact]
    public void NoCandidates_IsEscalate()
    {
        Assert.Equal(DecisionKind.Escalate, Policy.Decide(R(null)).Kind);
    }

    [Fact]
    public void WeakTopScore_IsEscalate()
    {
        Assert.Equal(DecisionKind.Escalate, Policy.Decide(R(Scope, C(0, 0.25))).Kind);
    }

    [Fact]
    public void MediumScore_IsClarify_WithOneOption()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.45)));
        Assert.Equal(DecisionKind.Clarify, decision.Kind);
        Assert.Single(decision.Candidates);
    }

    [Fact]
    public void MediumScores_ClarifyListsOnlyCandidatesAboveClarifyThreshold()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.55), C(1, 0.35), C(0, 0.2)));
        Assert.Equal(DecisionKind.Clarify, decision.Kind);
        Assert.Equal(2, decision.Candidates.Count);
    }

    [Fact]
    public void StrongSingleCandidate_IsAnswer()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.7)));
        Assert.Equal(DecisionKind.Answer, decision.Kind);
        Assert.Equal(Kb.Questions[0].Id, decision.Candidates[0].Question.Id);
    }

    [Fact]
    public void TwoCloseStrongCandidates_IsClarify()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.7), C(1, 0.65)));
        Assert.Equal(DecisionKind.Clarify, decision.Kind);
        Assert.Equal(2, decision.Candidates.Count);
    }

    [Fact]
    public void ClearLeaderEvenWithSecondStrong_IsAnswer()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.85), C(1, 0.6)));
        Assert.Equal(DecisionKind.Answer, decision.Kind);
    }

    [Fact]
    public void LeadersFromDifferentProducts_WithoutScope_AsksWhichProduct()
    {
        var decision = Policy.Decide(R(null, C(0, 0.55), C(1, 0.55)));
        Assert.Equal(DecisionKind.ClarifyProduct, decision.Kind);
        Assert.Equal(new[] { "2.1", "2.2" }, decision.Products.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void LeadersFromDifferentProducts_WithScope_IsPlainClarify()
    {
        var decision = Policy.Decide(R(Scope, C(0, 0.55), C(1, 0.55)));
        Assert.Equal(DecisionKind.Clarify, decision.Kind);
    }
}
