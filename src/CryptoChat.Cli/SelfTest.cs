using System.Text;
using System.Text.Json;
using CryptoChat.Core;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>Самопроверка: прогон acceptance.json и проверка, что заголовок каждого вопроса находит сам себя.</summary>
internal static class SelfTest
{
    internal sealed class AcceptanceCase
    {
        public string Q { get; set; } = "";
        public string? Expect { get; set; }
        public string? ExpectKind { get; set; }
    }

    /// <summary>Контрольные запросы из файла content/<name> (acceptance.json, paraphrases.json). Нет файла — пустой список.</summary>
    public static List<AcceptanceCase> LoadCases(string contentDir, string name = "acceptance.json")
    {
        var file = Path.Combine(contentDir, name);
        if (!File.Exists(file))
            return new List<AcceptanceCase>();
        return JsonSerializer.Deserialize<List<AcceptanceCase>>(File.ReadAllText(file, Encoding.UTF8),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    /// <summary>Прогоняет acceptance.json и заголовки всех вопросов. Возвращает провалы, число проверок и число кейсов.</summary>
    public static (List<string> Failures, int Checked, int Cases) CheckAcceptance(ChatEngine template, string contentDir)
    {
        var failures = new List<string>();
        int checkedCount = 0;

        var cases = LoadCases(contentDir);
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

        foreach (var question in template.KnowledgeBase.Questions)
        {
            checkedCount++;
            var reply = Fresh(template).Ask(question.Title);
            var firstId = FirstQuestionId(reply);
            if (firstId != question.Id)
                failures.Add($"заголовок {question.Id} «{question.Title}»: получено {reply.Kind} {firstId}");
        }

        return (failures, checkedCount, cases.Count);
    }

    public static int Run(ChatEngine template, string contentDir)
    {
        var (failures, checkedCount, cases) = CheckAcceptance(template, contentDir);

        Console.WriteLine(File.Exists(Path.Combine(contentDir, "acceptance.json"))
            ? $"Приёмочных вопросов: {cases}"
            : "Файл acceptance.json не найден, приёмочные вопросы пропущены.");
        Console.WriteLine($"Заголовков вопросов: {template.KnowledgeBase.Questions.Count}");

        foreach (var failure in failures)
            Console.WriteLine("  ПРОВАЛ: " + failure);

        Console.WriteLine(failures.Count == 0
            ? $"Самопроверка пройдена ({checkedCount} проверок)."
            : $"Самопроверка НЕ пройдена: {failures.Count} из {checkedCount}.");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Новый движок с той же базой: у каждой проверки своя чистая беседа.</summary>
    public static ChatEngine Fresh(ChatEngine template) =>
        new(template.KnowledgeBase, template.Config, semantic: template.Semantic);

    public static string? FirstQuestionId(ChatReply reply) => reply.Kind switch
    {
        ReplyKind.Answer => reply.Question?.Id,
        ReplyKind.Clarify => reply.Options.FirstOrDefault()?.Question?.Id,
        _ => null
    };

    /// <summary>Тип ответа, первый вопрос и его оценка — для сравнения режимов.</summary>
    public static (string Kind, string? First, double Score) Outcome(ChatEngine template, string query)
    {
        var reply = Fresh(template).Ask(query);
        var score = reply.Kind == ReplyKind.Answer ? reply.Score : reply.Options.FirstOrDefault()?.Score ?? 0.0;
        return (reply.Kind.ToString(), FirstQuestionId(reply), score);
    }

    public static bool Passed((string Kind, string? First, double Score) result, string? expect, string? expectKind) =>
        (expect == null || result.First == expect)
        && (expectKind == null || expectKind.Split('|').Contains(result.Kind));
}
