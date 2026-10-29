using CryptoChat.Core;
using CryptoChat.Core.Knowledge;

namespace CryptoChat.Cli;

/// <summary>Сравнение стеммера и морфологии: где результаты на контрольных запросах различаются.</summary>
internal static class Compare
{
    public static int Run(string contentDir)
    {
        var stemmer = ChatEngine.Create(contentDir, "stemmer", semantic: false);
        ChatEngine morph;
        try
        {
            morph = ChatEngine.Create(contentDir, "pymorphy3", semantic: false);
        }
        catch (ContentException ex)
        {
            Console.WriteLine("Сравнение невозможно: " + ex.Message);
            return 1;
        }

        var cases = SelfTest.LoadCases(contentDir)
            .Select(c => (Query: c.Q, c.Expect, c.ExpectKind))
            .Concat(stemmer.KnowledgeBase.Questions.Select(q => (Query: q.Title, Expect: (string?)q.Id, ExpectKind: (string?)null)))
            .ToList();

        int passedStemmer = 0, passedMorph = 0;
        var differences = new List<(string Query, string? Expected, (string, string?, double) A, (string, string?, double) B, bool GoodA, bool GoodB)>();
        foreach (var (query, expect, expectKind) in cases)
        {
            var a = SelfTest.Outcome(stemmer, query);
            var b = SelfTest.Outcome(morph, query);
            var goodA = SelfTest.Passed(a, expect, expectKind);
            var goodB = SelfTest.Passed(b, expect, expectKind);
            if (goodA) passedStemmer++;
            if (goodB) passedMorph++;
            if (a.Kind != b.Kind || a.First != b.First || goodA != goodB)
                differences.Add((query, expect ?? expectKind, a, b, goodA, goodB));
        }

        static string Show((string Kind, string? First, double Score) result, bool good) =>
            $"{(good ? "OK " : "ПРОВАЛ")} {result.Kind} {result.First ?? "-"} ({ChatEngine.Fmt(result.Score)})";

        Console.WriteLine($"Проверок: {cases.Count}");
        Console.WriteLine($"  стеммер:   прошло {passedStemmer}");
        Console.WriteLine($"  pymorphy3: прошло {passedMorph}");
        if (differences.Count == 0)
        {
            Console.WriteLine("Различий нет.");
            return 0;
        }

        Console.WriteLine($"\nРазличия ({differences.Count}):");
        foreach (var d in differences)
        {
            Console.WriteLine($"  «{d.Query}»  ожидалось: {d.Expected}");
            Console.WriteLine($"      стеммер:   {Show(d.A, d.GoodA)}");
            Console.WriteLine($"      pymorphy3: {Show(d.B, d.GoodB)}");
            Console.WriteLine($"      основы:    {string.Join(", ", stemmer.Text.Lemmatize(d.Query))}  |  " +
                              $"{string.Join(", ", morph.Text.Lemmatize(d.Query))}");
        }
        return 0;
    }
}
