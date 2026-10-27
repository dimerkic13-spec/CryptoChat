using System.Text.Json;
using CryptoChat.Core;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>Самопроверка: прогон acceptance.json и проверка, что заголовок каждого вопроса находит сам себя.</summary>
internal static class SelfTest
{
    private sealed class AcceptanceCase
    {
        public string Q { get; set; } = "";
        public string? Expect { get; set; }
        public string? ExpectKind { get; set; }
    }

    public static int Run(ChatEngine template, string contentDir)
    {
        var failures = new List<string>();
        int checkedCount = 0;

        var file = Path.Combine(contentDir, "acceptance.json");
        if (File.Exists(file))
        {
            var cases = JsonSerializer.Deserialize<List<AcceptanceCase>>(
                File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            foreach (var c in cases)
            {
                checkedCount++;
                var reply = Fresh(template).Ask(c.Q);
                var firstId = FirstQuestionId(reply);
                if (c.Expect != null && firstId != c.Expect)
                    failures.Add($"«{c.Q}»: ожидался {c.Expect}, получено {reply.Kind} {firstId}");
                if (c.ExpectKind != null && !c.ExpectKind.Split('|').Contains(reply.Kind.ToString()))
                    failures.Add($"«{c.Q}»: ожидался тип {c.ExpectKind}, получено {reply.Kind}");
            }
            Console.WriteLine($"Приёмочных вопросов: {cases.Count}");
        }
        else
        {
            Console.WriteLine("Файл acceptance.json не найден, приёмочные вопросы пропущены.");
        }

        foreach (var question in template.KnowledgeBase.Questions)
        {
            checkedCount++;
            var reply = Fresh(template).Ask(question.Title);
            var firstId = FirstQuestionId(reply);
            if (firstId != question.Id)
                failures.Add($"заголовок {question.Id} «{question.Title}»: получено {reply.Kind} {firstId}");
        }
        Console.WriteLine($"Заголовков вопросов: {template.KnowledgeBase.Questions.Count}");

        foreach (var failure in failures)
            Console.WriteLine("  ПРОВАЛ: " + failure);

        Console.WriteLine(failures.Count == 0
            ? $"Самопроверка пройдена ({checkedCount} проверок)."
            : $"Самопроверка НЕ пройдена: {failures.Count} из {checkedCount}.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static ChatEngine Fresh(ChatEngine template) => new(template.KnowledgeBase, template.Config);

    private static string? FirstQuestionId(ChatReply reply) => reply.Kind switch
    {
        ReplyKind.Answer => reply.Question?.Id,
        ReplyKind.Clarify => reply.Options.FirstOrDefault()?.Question?.Id,
        _ => null
    };
}
