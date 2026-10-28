using System.Text;
using System.Text.Json;
using CryptoChat.Core;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Проверка качества на большом наборе формулировок (content/synthetic.json: по 50 запросов на каждый вопрос).
/// Показывает, как часто бот находит нужный вопрос, где промахивается и где отвечает неверно —
/// по этой таблице подбираются ключевые слова, веса и синонимы.
/// </summary>
internal static class Synthetic
{
    public const string FileName = "synthetic.json";

    /// <summary>Чем закончился запрос.</summary>
    public enum Outcome
    {
        /// <summary>Сразу верный ответ.</summary>
        Answer,
        /// <summary>Переспросил, нужный вопрос первым в меню.</summary>
        First,
        /// <summary>Нужный вопрос есть в меню, но не первым.</summary>
        InMenu,
        /// <summary>Нужного вопроса нет в меню (меню тем, поддержка).</summary>
        Miss,
        /// <summary>Сразу ответил, но не на тот вопрос — самое опасное.</summary>
        WrongAnswer
    }

    public sealed record Result(string Expected, string Query, Outcome Outcome, string? Got);

    public static Dictionary<string, List<string>> Load(string contentDir)
    {
        var path = Path.Combine(contentDir, FileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path, Encoding.UTF8)) ?? new()
            : new();
    }

    public static Outcome Classify(ChatReply reply, string expected)
    {
        if (reply.Kind == ReplyKind.Answer)
            return reply.Question?.Id == expected ? Outcome.Answer : Outcome.WrongAnswer;
        var ids = reply.Options.Where(o => o.Question != null).Select(o => o.Question!.Id).ToList();
        if (ids.Count > 0 && ids[0] == expected)
            return Outcome.First;
        return ids.Contains(expected) ? Outcome.InMenu : Outcome.Miss;
    }

    public static List<Result> Evaluate(ChatEngine template, Dictionary<string, List<string>> cases)
    {
        var results = new List<Result>();
        foreach (var (expected, queries) in cases)
        {
            foreach (var query in queries)
            {
                var reply = SelfTest.Fresh(template).Ask(query);
                results.Add(new Result(expected, query, Classify(reply, expected), SelfTest.FirstQuestionId(reply)));
            }
        }
        return results;
    }

    public static int Run(ChatEngine template, string contentDir)
    {
        var cases = Load(contentDir);
        if (cases.Count == 0)
        {
            Console.WriteLine($"Файл {FileName} не найден в {contentDir}.");
            return 1;
        }

        var results = Evaluate(template, cases);
        string Line(IReadOnlyCollection<Result> rs)
        {
            int Count(Outcome o) => rs.Count(r => r.Outcome == o);
            string P(int n) => rs.Count == 0 ? "0%" : $"{100.0 * n / rs.Count:F1}%";
            var first = Count(Outcome.Answer) + Count(Outcome.First);
            return $"верно первым {first,4} ({P(first)}) | сразу ответ {Count(Outcome.Answer),4} | в меню не первым {Count(Outcome.InMenu),4} | " +
                   $"промах {Count(Outcome.Miss),4} ({P(Count(Outcome.Miss))}) | НЕВЕРНЫЙ ОТВЕТ {Count(Outcome.WrongAnswer),3}";
        }

        Console.WriteLine($"Морфология: {template.Morphology}; поиск по смыслу: {template.Semantic?.Name ?? "выкл"}");
        Console.WriteLine($"Запросов: {results.Count} (вопросов: {cases.Count})");
        Console.WriteLine("Всего: " + Line(results));
        Console.WriteLine();
        Console.WriteLine("По вопросам (верно первым / промахов):");
        var titles = template.KnowledgeBase.Questions.ToDictionary(q => q.Id, q => q.Title);
        foreach (var group in results.GroupBy(r => r.Expected).OrderBy(g => g.Key, Core.Retrieval.IdComparer.Instance))
        {
            var rs = group.ToList();
            var first = rs.Count(r => r.Outcome is Outcome.Answer or Outcome.First);
            var miss = rs.Count(r => r.Outcome == Outcome.Miss);
            var title = titles.GetValueOrDefault(group.Key, "");
            Console.WriteLine($"  {group.Key,-8} {first,3}/{rs.Count,-3} промахов {miss,3}  {(title.Length > 55 ? title[..55] + "…" : title)}");
        }

        var wrong = results.Where(r => r.Outcome == Outcome.WrongAnswer).ToList();
        if (wrong.Count > 0)
        {
            Console.WriteLine("\nНеверные прямые ответы:");
            foreach (var r in wrong)
                Console.WriteLine($"  ожидался {r.Expected}, ответ {r.Got}: «{r.Query}»");
        }
        return 0;
    }
}
