using CryptoChat.Core.Dialog;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using Xunit;

namespace CryptoChat.Tests;

public class GuidedPolicyTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Build(TestSupport.TwoProducts());
    private static readonly DialogPolicy Policy = new(
        new AppConfig { Dialog = new DialogConfig { Guided = true } }, Kb);
    private static readonly string[] SomeLemmas = { "x" };

    private static Candidate C(int questionIndex, double score) => new(Kb.Questions[questionIndex], score);

    private static RetrievalResult R(params Candidate[] candidates) =>
        new(SomeLemmas, Kb.Categories["2.1"], 0, candidates);

    [Fact]
    public void NoCandidates_ClarifiesByBranch()
    {
        Assert.Equal(DecisionKind.ClarifyBranch, Policy.Decide(R()).Kind);
    }

    [Fact]
    public void WeakTopScore_ClarifiesByBranch()
    {
        Assert.Equal(DecisionKind.ClarifyBranch, Policy.Decide(R(C(0, 0.25))).Kind);
    }

    [Fact]
    public void OneToThreeFittingQuestions_AreOffered()
    {
        var decision = Policy.Decide(R(C(0, 0.45), C(1, 0.4)));
        Assert.Equal(DecisionKind.Clarify, decision.Kind);
        Assert.Equal(2, decision.Candidates.Count);
    }

    [Fact]
    public void MoreThanThreeFittingQuestions_ClarifiesByBranch()
    {
        var decision = Policy.Decide(R(C(0, 0.5), C(1, 0.45), C(0, 0.4), C(1, 0.35)));
        Assert.Equal(DecisionKind.ClarifyBranch, decision.Kind);
    }

    [Fact]
    public void SingleStrongQuestion_IsAnswered()
    {
        Assert.Equal(DecisionKind.Answer, Policy.Decide(R(C(0, 0.8), C(1, 0.3))).Kind);
    }

    [Fact]
    public void DefaultConfig_StillEscalatesImmediately()
    {
        var plain = new DialogPolicy(new AppConfig(), Kb);
        Assert.Equal(DecisionKind.Escalate, plain.Decide(R(C(0, 0.25))).Kind);
    }
}
