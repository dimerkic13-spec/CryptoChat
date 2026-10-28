using CryptoChat.Cli;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using CryptoChat.Core.Text;

namespace CryptoChat.Tests;

public class FuzzyMatcherTests
{
    private static readonly FuzzyMatcher Fuzzy = new(threshold: 85, minLength: 5, weight: 0.8);

    [Theory]
    [InlineData("рутокин", "рутокен")]
    [InlineData("кабинед", "кабинет")]
    [InlineData("дубликт", "дубликат")]
    [InlineData("лицензя", "лицензи")]
    public void Typos_AreSimilar(string typo, string word) => Assert.True(Fuzzy.Matches(typo, word));

    [Theory]
    [InlineData("договор", "разговор")]
    [InlineData("кабинет", "кластер")]
    [InlineData("пароль", "портал")]
    public void DifferentWords_AreNotSimilar(string a, string b) => Assert.False(Fuzzy.Matches(a, b));

    [Fact]
    public void SameRootWords_AreLinked()
    {
        // «оплата» и «оплатить» — одно слово по смыслу; со стеммером они и так совпадали.
        Assert.True(Fuzzy.Matches("оплата", "оплатить"));
    }

    [Fact]
    public void ShortWords_AreNeverFuzzy()
    {
        Assert.False(Fuzzy.Matches("пинг", "пин"));
        Assert.False(new FuzzyMatcher(threshold: 50, minLength: 5).Matches("ключ", "клюв"));
    }

    [Theory]
    [InlineData("рутокин", "рутокен", 85.71428571428572)]
    [InlineData("abc", "abc", 100)]
    [InlineData("", "", 100)]
    [InlineData("abc", "", 0)]
    [InlineData("договор", "разговор", 66.66666666666667)]
    public void Ratio_IsTheSameAsRapidFuzz(string a, string b, double expected)
    {
        Assert.Equal(expected, FuzzyMatcher.Ratio(a, b), 9);
    }

    [Fact]
    public void Typo_GivesReducedWeight()
    {
        var keywords = new[] { new Keyword("рутокен", 6), new Keyword("пароль", 4) };
        var exact = Scorer.Evaluate(keywords, new[] { "рутокен" }, 4, Fuzzy).Score;
        var (typo, matched) = Scorer.Evaluate(keywords, new[] { "рутокин" }, 4, Fuzzy);

        Assert.Equal(0.6, exact, 9);
        Assert.Equal(0.6 * 0.8, typo, 9);
        Assert.Equal(1, matched);
        Assert.Equal(0.0, Scorer.Evaluate(keywords, new[] { "рутокин" }, 4).Score);
        Assert.Equal(new[] { ("рутокин", "рутокен") }, Scorer.TypoMatches(keywords, new[] { "рутокин" }, 4, Fuzzy));
    }

    [Fact]
    public void DisabledInConfig_GivesNull()
    {
        Assert.Null(FuzzyMatcher.FromConfig(new FuzzyConfig { Enabled = false }));
        Assert.NotNull(FuzzyMatcher.FromConfig(new FuzzyConfig { Enabled = true }));
    }
}

public class FuzzyEngineTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();

    [Fact]
    public void TypoInQuery_StillFindsQuestion()
    {
        var config = new AppConfig();
        config.Fuzzy.Enabled = true;
        var reply = new ChatEngine(KnowledgeBase.Build(TestContent.Valid()), config).Ask("класстер ngate");
        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("2.1.1", reply.Question?.Id);
    }

    [Fact]
    public void RealContent_WithFuzzy_PassesAcceptance()
    {
        var engine = ChatEngine.Create(ContentDir, "stemmer", semantic: false);
        Assert.True(engine.Config.Fuzzy.Enabled, "в content/config.json учёт опечаток включён");
        var (failures, _, _) = SelfTest.CheckAcceptance(engine, ContentDir);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("забыл пароль от рутокина", "1.3.2.1")]
    [InlineData("забыл пароль от кабинед", "1.3.3.2")]
    [InlineData("нужен дубликт лицензии", "1.1.4.2")]
    [InlineData("присоеденится к эдо", "1.2.4.1")]
    public void RealTypos_FindRightQuestion(string query, string expected)
    {
        var reply = ChatEngine.Create(ContentDir, "stemmer", semantic: false).Ask(query);
        Assert.Equal(expected, SelfTest.FirstQuestionId(reply));
    }
}

/// <summary>Похожие вопросы вместо меню тем; переходы по меню не ведут в поддержку.</summary>
public class SuggestionAndNavigationTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();
    private static ChatEngine RealEngine() => ChatEngine.Create(ContentDir, "stemmer", semantic: false);

    [Fact]
    public void NearMiss_IsSuggestedInsteadOfTopics()
    {
        var reply = RealEngine().Ask("Я не могу найти номер лицензии");
        Assert.Equal(ReplyKind.Clarify, reply.Kind);
        Assert.Contains("Возможно, вы имели в виду", reply.Text);
        Assert.Equal("1.3.1.2", reply.Options[0].Question?.Id);
    }

    [Fact]
    public void Suggestions_CanBeTurnedOff()
    {
        var engine = RealEngine();
        engine.Config.Dialog.SuggestScore = 0;
        Assert.Equal(ReplyKind.ClarifyBranch, engine.Ask("Я не могу найти номер лицензии").Kind);
    }

    [Fact]
    public void MenuNavigation_NeverEscalates()
    {
        var engine = RealEngine();
        for (int i = 0; i < 4; i++)
            Assert.Equal(ReplyKind.ClarifyBranch, engine.Ask("абракадабра").Kind);
        // Четыре неудачных вопроса подряд — счётчик на пределе, но человек идёт по меню сам.
        Assert.Equal(ReplyKind.ClarifyBranch, engine.Ask("1").Kind);
        var leaf = engine.Ask("1");
        Assert.Equal(ReplyKind.ClarifyBranch, leaf.Kind);
        Assert.All(leaf.Options, o => Assert.NotNull(o.Question));
        Assert.Equal(ReplyKind.Answer, engine.Ask("1").Kind);
    }

    [Fact]
    public void FreeText_StillEscalatesAfterLimit()
    {
        var engine = RealEngine();
        for (int i = 0; i < 4; i++)
            engine.Ask("абракадабра");
        Assert.Equal(ReplyKind.Escalate, engine.Ask("абракадабра").Kind);
    }

    [Fact]
    public void SessionCommand_ShowsSessionNumber()
    {
        var engine = RealEngine();
        Assert.Contains("выключен", engine.Ask("/сессия").Text);
        engine.SessionId = "20261028-084012-7f3a";
        Assert.Contains("20261028-084012-7f3a", engine.Ask("/сессия").Text);
    }

    [Fact]
    public void Paraphrases_FileIsReadable()
    {
        var cases = SelfTest.LoadCases(ContentDir, "paraphrases.json");
        Assert.NotEmpty(cases);
        Assert.All(cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Q)));
    }
}
