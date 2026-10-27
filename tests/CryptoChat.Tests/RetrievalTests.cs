using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using Xunit;

namespace CryptoChat.Tests;

public class RetrievalTests
{
    private static readonly AppConfig Config = new();

    [Fact]
    public void ScopeResolver_FindsProductByItsKeywords()
    {
        var kb = KnowledgeBase.Build(TestContent.Valid());
        var scope = new ScopeResolver(kb, Config).Resolve(TestSupport.Lemmas(kb, "кластер ngate"));
        Assert.Equal("2.1", scope.Category?.Id);
    }

    [Fact]
    public void ScopeResolver_NoMatch_GivesNoScope()
    {
        var kb = KnowledgeBase.Build(TestContent.Valid());
        var scope = new ScopeResolver(kb, Config).Resolve(TestSupport.Lemmas(kb, "лицензия"));
        Assert.Null(scope.Category);
    }

    [Fact]
    public void ScopeResolver_TieBetweenProducts_GivesNoScope()
    {
        var kb = KnowledgeBase.Build(TestSupport.TwoProducts());
        var scope = new ScopeResolver(kb, Config).Resolve(TestSupport.Lemmas(kb, "лицензия"));
        Assert.Null(scope.Category);
    }

    [Fact]
    public void ScopeResolver_ClearLeader_WinsOverRival()
    {
        var kb = KnowledgeBase.Build(TestSupport.TwoProducts());
        var scope = new ScopeResolver(kb, Config).Resolve(TestSupport.Lemmas(kb, "csp лицензия"));
        Assert.Equal("2.1", scope.Category?.Id);
    }

    [Fact]
    public void Retriever_AppliesScopeBoost()
    {
        var kb = KnowledgeBase.Build(TestContent.Valid());
        var result = new KeywordRetriever(kb, Config).Search(TestSupport.Lemmas(kb, "ngate"));

        Assert.Equal("2.1", result.Scope?.Id);
        Assert.Single(result.Candidates);
        Assert.Equal("2.1.1", result.Candidates[0].Question.Id);
        Assert.Equal(0.75, result.Candidates[0].Score, 3); // 6/10 * 1.25
    }

    [Fact]
    public void Retriever_WithoutScope_SearchesWholeBase()
    {
        var kb = KnowledgeBase.Build(TestContent.Valid());
        var result = new KeywordRetriever(kb, Config).Search(TestSupport.Lemmas(kb, "купить лицензию"));

        Assert.Null(result.Scope);
        Assert.Equal("1.1.1", result.Candidates[0].Question.Id);
    }

    [Fact]
    public void Retriever_ForcedScope_LimitsCandidates()
    {
        var kb = KnowledgeBase.Build(TestSupport.TwoProducts());
        var retriever = new KeywordRetriever(kb, Config);

        var result = retriever.Search(TestSupport.Lemmas(kb, "лицензия"), kb.Categories["2.2"]);

        Assert.Equal("2.2", result.Scope?.Id);
        Assert.Single(result.Candidates);
        Assert.Equal("2.2.1", result.Candidates[0].Question.Id);
    }

    [Fact]
    public void Retriever_DraftQuestionsAreIgnored()
    {
        var content = TestContent.Valid();
        content.Questions[0].Status = "draft";
        var kb = KnowledgeBase.Build(content);

        var result = new KeywordRetriever(kb, Config).Search(TestSupport.Lemmas(kb, "купить лицензию"));

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Retriever_NoLemmas_GivesNoCandidates()
    {
        var kb = KnowledgeBase.Build(TestContent.Valid());
        var result = new KeywordRetriever(kb, Config).Search(Array.Empty<string>());
        Assert.Empty(result.Candidates);
    }
}
