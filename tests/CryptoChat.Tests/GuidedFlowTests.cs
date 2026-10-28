using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using Xunit;

namespace CryptoChat.Tests;

/// <summary>Управляемый диалог на настоящей базе: уточнение по веткам, отзыв, перевод в поддержку.</summary>
public class GuidedFlowTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();

    private static ChatEngine NewEngine() =>
        new(KnowledgeLoader.Load(ContentDir), AppConfig.Load(ContentDir));

    [Fact]
    public void RealConfig_TurnsOnGuidedDialog()
    {
        var config = AppConfig.Load(ContentDir);
        Assert.True(config.Dialog.Guided);
        Assert.Equal(4, config.Dialog.EscalateAfterSteps);
        Assert.Equal(0.6, config.Thresholds.Answer, 6);
        Assert.Equal(0.3, config.Thresholds.Clarify, 6);
    }

    [Fact]
    public void UnknownQuestion_AsksTopic_ThenBlock_ThenQuestion_ThenAnswers()
    {
        var engine = NewEngine();

        var topics = engine.Ask("абракадабра");
        Assert.Equal(ReplyKind.ClarifyBranch, topics.Kind);
        Assert.Equal(3, topics.Options.Count);
        Assert.Equal("1.1", topics.Options[0].Branch?.Id);
        Assert.All(topics.Options, o => Assert.StartsWith("1.", o.Branch!.Id));
        Assert.Contains("0. Ничего не подходит", topics.Text);

        var blocks = engine.Ask("1");
        Assert.Equal(ReplyKind.ClarifyBranch, blocks.Kind);
        Assert.Equal("1.1.1", blocks.Options[0].Branch?.Id);

        var questions = engine.Ask("2");
        Assert.Equal(ReplyKind.ClarifyBranch, questions.Kind);
        Assert.Equal(3, questions.Options.Count);
        Assert.Equal("1.1.2.1", questions.Options[0].Question?.Id);

        var answer = engine.Ask("1");
        Assert.Equal(ReplyKind.Answer, answer.Kind);
        Assert.Equal("1.1.2.1", answer.Question?.Id);
        Assert.Contains("0. Обратиться в техподдержку", answer.Text);
        Assert.Equal(2, answer.Options.Count);
    }

    [Fact]
    public void ZeroInMenu_GoesToSupportImmediately()
    {
        var engine = NewEngine();
        engine.Ask("абракадабра");

        var reply = engine.Ask("0");

        Assert.Equal(ReplyKind.Escalate, reply.Kind);
        Assert.Contains("support.cryptopro.ru", reply.Text);
    }

    [Fact]
    public void SupportIsOfferedOnlyAfterSeveralClarificationSteps()
    {
        var engine = NewEngine();

        for (int i = 0; i < 4; i++)
            Assert.Equal(ReplyKind.ClarifyBranch, engine.Ask("абракадабра").Kind);

        var fifth = engine.Ask("абракадабра");
        Assert.Equal(ReplyKind.Escalate, fifth.Kind);

        // После перевода в поддержку счётчик начинается заново.
        Assert.Equal(ReplyKind.ClarifyBranch, engine.Ask("абракадабра").Kind);
    }

    [Fact]
    public void ConfidentAnswer_ComesWithSimilarQuestionsFeedbackAndSupport()
    {
        var reply = NewEngine().Ask("забыл логин и пароль от кабинета");

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("1.3.3.2", reply.Question?.Id);
        Assert.Contains("Ответ помог?", reply.Text);
        Assert.Contains("0. Обратиться в техподдержку", reply.Text);
        Assert.Equal("1.3.2.1", reply.Options[0].Question?.Id);
    }

    [Fact]
    public void WeakQuestion_ShowsFittingQuestions_ThenAnswerWithFeedback_Yes()
    {
        var engine = NewEngine();
        var given = new List<FeedbackEntry>();
        engine.FeedbackGiven += given.Add;

        var menu = engine.Ask("Забыл пароль от рутокена");
        Assert.Equal(ReplyKind.Clarify, menu.Kind);
        Assert.Equal("1.3.2.1", menu.Options[0].Question?.Id);
        // С 0.2.6a «пароль от кабинета» сюда больше не попадает: у вопроса 1.3.3.2 больше ключей,
        // и одно слово «пароль» не дотягивает до порога уточнения. Вопрос про токен — первый.

        var answer = engine.Ask("1");
        Assert.Equal(ReplyKind.Answer, answer.Kind);
        Assert.Equal("1.3.2.1", answer.Question?.Id);

        var thanks = engine.Ask("да");
        Assert.Equal(ReplyKind.Feedback, thanks.Kind);

        var entry = Assert.Single(given);
        Assert.True(entry.Helpful);
        Assert.Equal("1.3.2.1", entry.QuestionId);
        Assert.Equal("Забыл пароль от рутокена", entry.Query);
    }

    [Fact]
    public void NegativeFeedback_KeepsSimilarQuestionsAndOffersSupport()
    {
        var engine = NewEngine();
        var given = new List<FeedbackEntry>();
        engine.FeedbackGiven += given.Add;

        engine.Ask("забыл логин и пароль от кабинета");
        var reply = engine.Ask("нет");

        Assert.Equal(ReplyKind.Feedback, reply.Kind);
        Assert.Contains("0. Обратиться в техподдержку", reply.Text);
        Assert.Single(reply.Options);
        Assert.False(Assert.Single(given).Helpful);

        var next = engine.Ask("1");
        Assert.Equal(ReplyKind.Answer, next.Kind);
        Assert.Equal("1.3.2.1", next.Question?.Id);
    }

    [Fact]
    public void YesNoWithoutAnswer_AreOrdinaryQuestions()
    {
        var engine = NewEngine();
        var given = new List<FeedbackEntry>();
        engine.FeedbackGiven += given.Add;

        engine.Ask("да");

        Assert.Empty(given);
    }
}
