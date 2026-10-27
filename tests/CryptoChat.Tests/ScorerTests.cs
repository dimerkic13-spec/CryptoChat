using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using Xunit;

namespace CryptoChat.Tests;

public class ScorerTests
{
    [Theory]
    [InlineData("истекл", "истек", true)]     // длинные основы: достаточно общего начала
    [InlineData("лицензи", "лицензи", true)]  // равные основы
    [InlineData("куп", "купил", false)]       // короткая основа совпадает только полностью
    [InlineData("пин", "пинг", false)]
    [InlineData("порт", "ipsec", false)]
    public void Match_FollowsPrefixRule(string lemma, string keyword, bool expected)
    {
        Assert.Equal(expected, Scorer.Match(lemma, keyword, 4));
    }

    [Fact]
    public void Evaluate_IsMatchedWeightOverTotalWeight()
    {
        var keywords = new List<Keyword> { new("куп", 5), new("лицензи", 3), new("оплат", 2) };
        var (score, matched) = Scorer.Evaluate(keywords, new[] { "куп", "лицензи" }, 4);
        Assert.Equal(0.8, score, 6);
        Assert.Equal(2, matched);
    }

    [Fact]
    public void Evaluate_CountsEachKeywordOnce()
    {
        var keywords = new List<Keyword> { new("лицензи", 4), new("оплат", 4) };
        var (score, matched) = Scorer.Evaluate(keywords, new[] { "лицензи", "лицензи", "лицензиат" }, 4);
        Assert.Equal(0.5, score, 6);
        Assert.Equal(1, matched);
    }

    [Fact]
    public void Evaluate_NoKeywords_IsZero()
    {
        var (score, matched) = Scorer.Evaluate(new List<Keyword>(), new[] { "куп" }, 4);
        Assert.Equal(0.0, score);
        Assert.Equal(0, matched);
    }

    [Fact]
    public void IdComparer_SortsNumerically()
    {
        var ids = new[] { "1.1.1.10", "1.1.1.2", "1.1.1.1", "1.2.1.1" };
        var sorted = ids.OrderBy(i => i, IdComparer.Instance).ToArray();
        Assert.Equal(new[] { "1.1.1.1", "1.1.1.2", "1.1.1.10", "1.2.1.1" }, sorted);
    }
}
