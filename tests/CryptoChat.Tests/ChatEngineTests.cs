using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using Xunit;

namespace CryptoChat.Tests;

public class ChatEngineTests
{
    private static ChatEngine Engine(ContentSet? content = null) =>
        new(KnowledgeBase.Build(content ?? TestContent.Valid()), new AppConfig());

    [Fact]
    public void ConfidentQuestion_GetsAnswerWithSource()
    {
        var reply = Engine().Ask("как купить лицензию");

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("1.1.1", reply.Question?.Id);
        Assert.Contains("Текст ответа.", reply.Text);
        Assert.Contains("https://cryptopro.ru/faq/test#test", reply.Text);
        Assert.Contains("2026-10-09", reply.Text);
    }

    [Fact]
    public void WeakQuestion_AsksToConfirm_ThenShowsAnswer()
    {
        var engine = Engine();

        var ask = engine.Ask("лицензия");
        Assert.Equal(ReplyKind.Clarify, ask.Kind);
        Assert.Single(ask.Options);

        var answer = engine.Ask("1");
        Assert.Equal(ReplyKind.Answer, answer.Kind);
        Assert.Equal("1.1.1", answer.Question?.Id);
    }

    [Fact]
    public void ZeroInMenu_Escalates()
    {
        var engine = Engine();
        engine.Ask("лицензия");

        var reply = engine.Ask("0");

        Assert.Equal(ReplyKind.Escalate, reply.Kind);
        Assert.Contains("support.cryptopro.ru", reply.Text);
    }

    [Fact]
    public void AnyOtherInputInMenu_IsNewQuestion()
    {
        var engine = Engine();
        engine.Ask("лицензия");

        var reply = engine.Ask("кластер ngate");

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("2.1.1", reply.Question?.Id);
    }

    [Fact]
    public void NumberWithoutMenu_IsNotUnderstood()
    {
        Assert.Equal(ReplyKind.NotUnderstood, Engine().Ask("1").Kind);
    }

    [Fact]
    public void Gibberish_Escalates()
    {
        Assert.Equal(ReplyKind.Escalate, Engine().Ask("абракадабра").Kind);
    }

    [Fact]
    public void OnlyStopWords_IsNotUnderstood()
    {
        Assert.Equal(ReplyKind.NotUnderstood, Engine().Ask("как и что").Kind);
    }

    [Theory]
    [InlineData("выход")]
    [InlineData("exit")]
    [InlineData("quit")]
    [InlineData("/выход")]
    public void ExitWords_ReturnExit(string input)
    {
        var reply = Engine().Ask(input);
        Assert.Equal(ReplyKind.Exit, reply.Kind);
        Assert.Contains("До свидания", reply.Text);
    }

    [Fact]
    public void ProductClarification_ThenChoice_GivesAnswerFromThatProduct()
    {
        var engine = Engine(TestSupport.TwoProducts());

        var ask = engine.Ask("лицензия");
        Assert.Equal(ReplyKind.ClarifyProduct, ask.Kind);
        Assert.Equal(2, ask.Options.Count);
        Assert.Equal("КриптоПро NGate", ask.Options[1].Label);

        var answer = engine.Ask("2");
        Assert.Equal(ReplyKind.Answer, answer.Kind);
        Assert.Equal("2.2.1", answer.Question?.Id);
    }

    [Fact]
    public void Debug_IsShownOnlyWhenEnabled()
    {
        var engine = Engine();
        Assert.Null(engine.Ask("как купить лицензию").Debug);

        Assert.Equal(ReplyKind.Command, engine.Ask("/отладка вкл").Kind);
        var debug = engine.Ask("как купить лицензию").Debug;
        Assert.NotNull(debug);
        Assert.Contains("леммы", debug);
        Assert.Contains("1.1.1", debug);

        engine.Ask("/отладка выкл");
        Assert.Null(engine.Ask("как купить лицензию").Debug);
    }

    [Fact]
    public void Commands_Work()
    {
        var engine = Engine();

        Assert.Contains("/темы", engine.Ask("/помощь").Text);
        var tree = engine.Ask("/темы").Text;
        Assert.Contains("1.1 Лицензии (1)", tree);
        Assert.Contains("2.1 NGate (1)", tree);
        Assert.Contains("Как купить лицензию?", engine.Ask("/темы 1.1").Text);
        Assert.Contains("не найден", engine.Ask("/темы 9.9").Text);
        Assert.Contains("Неизвестная команда", engine.Ask("/что-то").Text);
    }
}
