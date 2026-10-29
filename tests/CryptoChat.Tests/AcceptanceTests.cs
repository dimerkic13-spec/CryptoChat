using System.Text.Json;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using Xunit;

namespace CryptoChat.Tests;

/// <summary>Прогон настоящих вопросов по настоящей базе из каталога content/.</summary>
public class AcceptanceTests
{
    private sealed class AcceptanceCase
    {
        public string Q { get; set; } = "";
        public string? Expect { get; set; }
        public string? ExpectKind { get; set; }
    }

    private static readonly string ContentDir = TestSupport.FindContentDir();

    private static string? FirstQuestionId(ChatReply reply) => reply.Kind switch
    {
        ReplyKind.Answer => reply.Question?.Id,
        ReplyKind.Clarify => reply.Options.FirstOrDefault()?.Question?.Id,
        _ => null
    };

    [Fact]
    public void AllAcceptanceCases_Pass()
    {
        var kb = KnowledgeLoader.Load(ContentDir);
        var config = AppConfig.Load(ContentDir);
        var json = File.ReadAllText(Path.Combine(ContentDir, "acceptance.json"));
        var cases = JsonSerializer.Deserialize<List<AcceptanceCase>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var failures = new List<string>();
        foreach (var c in cases)
        {
            var reply = new ChatEngine(kb, config).Ask(c.Q);
            var firstId = FirstQuestionId(reply);

            if (c.Expect != null && firstId != c.Expect)
                failures.Add($"«{c.Q}»: ожидался {c.Expect}, получено {reply.Kind} {firstId}");
            if (c.ExpectKind != null && !c.ExpectKind.Split('|').Contains(reply.Kind.ToString()))
                failures.Add($"«{c.Q}»: ожидался тип {c.ExpectKind}, получено {reply.Kind}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(cases.Count >= 40);
    }

    [Fact]
    public void EveryQuestionTitle_FindsItself()
    {
        var kb = KnowledgeLoader.Load(ContentDir);
        var config = AppConfig.Load(ContentDir);

        var failures = new List<string>();
        foreach (var question in kb.Questions)
        {
            var reply = new ChatEngine(kb, config).Ask(question.Title);
            var firstId = FirstQuestionId(reply);
            if (firstId != question.Id)
                failures.Add($"{question.Id} «{question.Title}»: получено {reply.Kind} {firstId}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RealConfig_HasCautiousThresholds()
    {
        var config = AppConfig.Load(ContentDir);
        Assert.Equal(0.65, config.Thresholds.Answer, 6);
        Assert.Equal(0.3, config.Thresholds.Clarify, 6);
        Assert.False(config.Logging.Unanswered);
    }
}
