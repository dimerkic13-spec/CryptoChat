using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using CryptoChat.Core;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using CryptoChat.Core.Semantic;

namespace CryptoChat.Cli;

/// <summary>Команды для поиска по смыслу: проверка модели, сравнение с моделью и без, выгрузка оценок для настройки.</summary>
internal static class SemanticCommands
{
    public const string DumpFile = "semantic_dump.json";

    /// <summary>Число как его печатает Python: 0.3, 5.0, 1e-05.</summary>
    internal static string Py(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e16
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    // ---------- Модель ----------

    public static int DownloadModel(string contentDir)
    {
        var config = AppConfig.Load(contentDir);
        var folder = OnnxEncoder.ModelFolder(SemanticFactory.CacheDir(config.Semantic, ConsoleApp.AppDirectory()), config.Semantic.Model);
        Console.WriteLine("C#-версия не скачивает модель сама: модель один раз выгружается из Python-версии в формат ONNX.");
        Console.WriteLine();
        Console.WriteLine("В папке Python-версии (там, где .venv с PyTorch) выполните:");
        Console.WriteLine($"  .\\.venv\\Scripts\\python.exe <папка C#-версии>\\tools\\export_onnx.py --model {config.Semantic.Model} --cache-dir models --out <папка C#-версии>\\models");
        Console.WriteLine();
        Console.WriteLine($"Модель должна оказаться здесь: {folder}");
        Console.WriteLine("Проверка после выгрузки: CryptoChat --check-model");
        return 0;
    }

    /// <summary>Сверяет модель с эталоном, записанным при выгрузке из Python: токены должны совпасть, векторы — почти совпасть.</summary>
    public static int CheckModel(string contentDir)
    {
        var config = AppConfig.Load(contentDir);
        var folder = OnnxEncoder.ModelFolder(SemanticFactory.CacheDir(config.Semantic, ConsoleApp.AppDirectory()), config.Semantic.Model);
        OnnxEncoder encoder;
        try
        {
            encoder = OnnxEncoder.Load(folder);
        }
        catch (SemanticUnavailableException ex)
        {
            Console.WriteLine("Ошибка: " + ex.Message);
            return 1;
        }

        using (encoder)
        {
            Console.WriteLine($"Модель: {encoder.Name} ({folder})");
            Console.WriteLine($"Размер вектора: {encoder.Info.Dimension}, длина текста до {encoder.Info.MaxSeqLength} токенов");
            if (encoder.Info.Reference.Count == 0)
            {
                Console.WriteLine("В cryptochat-model.json нет эталонных фраз — сверять не с чем.");
                return 1;
            }

            int tokenErrors = 0, vectorErrors = 0;
            double worst = 1.0;
            foreach (var reference in encoder.Info.Reference)
            {
                var ids = encoder.Tokenize(reference.Text);
                if (!ids.SequenceEqual(reference.Ids))
                {
                    tokenErrors++;
                    Console.WriteLine($"  токены не совпали: «{reference.Text}»");
                    Console.WriteLine($"      Python: {string.Join(" ", reference.Ids)}");
                    Console.WriteLine($"      C#:     {string.Join(" ", ids)}");
                }
                var vector = encoder.Encode(new[] { reference.Text })[0];
                var cosine = Cosine(vector, reference.Vector);
                worst = Math.Min(worst, cosine);
                if (cosine < 0.9999)
                {
                    vectorErrors++;
                    Console.WriteLine($"  вектор отличается: «{reference.Text}» (похожесть с эталоном {cosine:F6})");
                }
            }

            Console.WriteLine($"Эталонных фраз: {encoder.Info.Reference.Count}; худшая похожесть с вектором Python: {worst:F6}");
            if (tokenErrors == 0 && vectorErrors == 0)
            {
                Console.WriteLine("Модель работает так же, как в Python-версии.");
                return 0;
            }
            Console.WriteLine($"Расхождения: токены — {tokenErrors}, векторы — {vectorErrors}. Пришлите этот вывод.");
            return 1;
        }
    }

    private static double Cosine(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return a.Count != b.Count || na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

    // ---------- Сравнение с моделью и без ----------

    /// <summary>(запрос, ожидаемый вопрос, ожидаемый тип, откуда): acceptance.json, paraphrases.json, заголовки.</summary>
    private static List<(string Query, string? Expect, string? ExpectKind, string Source)> Cases(string contentDir, ChatEngine engine)
    {
        var cases = new List<(string, string?, string?, string)>();
        foreach (var name in new[] { "acceptance.json", "paraphrases.json" })
            cases.AddRange(SelfTest.LoadCases(contentDir, name).Select(c => (c.Q, c.Expect, c.ExpectKind, name)));
        cases.AddRange(engine.KnowledgeBase.Questions.Select(q => (q.Title, (string?)q.Id, (string?)null, "заголовки")));
        return cases;
    }

    private static ChatEngine? LoadWithModel(string contentDir, string? morphology, string action)
    {
        Console.WriteLine("Загружаю модель...");
        var engine = ChatEngine.Create(contentDir, morphology, semantic: true, appDir: ConsoleApp.AppDirectory());
        if (engine.Semantic != null)
            return engine;
        Console.WriteLine($"{action} невозможно: " + (engine.SemanticNote ?? "поиск по смыслу выключен."));
        return null;
    }

    /// <summary>Показывает, где поиск по смыслу помог, а где помешал. По выводу подбираются настройки semantic.</summary>
    public static int CompareSemantic(string contentDir, string? morphology)
    {
        var withModel = LoadWithModel(contentDir, morphology, "Сравнение");
        if (withModel == null)
            return 1;
        var without = new ChatEngine(withModel.KnowledgeBase, withModel.Config);
        var sem = withModel.Semantic!;
        Console.WriteLine($"Модель: {sem.Name}; low={Py(sem.Low)}, high={Py(sem.High)}, weight={Py(sem.Weight)}");

        var totals = new Dictionary<string, (int N, int GoodA, int GoodB)>();
        var better = new List<(string, string?, (string, string?, double), (string, string?, double))>();
        var worse = new List<(string, string?, (string, string?, double), (string, string?, double))>();
        var changed = new List<(string, string?, (string, string?, double), (string, string?, double))>();

        foreach (var (query, expect, expectKind, source) in Cases(contentDir, withModel))
        {
            var a = SelfTest.Outcome(without, query);
            var b = SelfTest.Outcome(withModel, query);
            var goodA = SelfTest.Passed(a, expect, expectKind);
            var goodB = SelfTest.Passed(b, expect, expectKind);
            var t = totals.GetValueOrDefault(source);
            totals[source] = (t.N + 1, t.GoodA + (goodA ? 1 : 0), t.GoodB + (goodB ? 1 : 0));
            if (goodB && !goodA)
                better.Add((query, expect, a, b));
            else if (goodA && !goodB)
                worse.Add((query, expect, a, b));
            else if (a.Kind != b.Kind || a.First != b.First)
                changed.Add((query, expect, a, b));
        }

        Console.WriteLine("\nПрошло проверок:          без модели   с моделью");
        foreach (var (source, (n, goodA, goodB)) in totals)
            Console.WriteLine($"  {source,-22} {goodA,4} из {n,-4} {goodB,4} из {n}");

        void Show(string title, List<(string Query, string? Expect, (string Kind, string? First, double Score) A, (string Kind, string? First, double Score) B)> rows)
        {
            if (rows.Count == 0)
                return;
            Console.WriteLine($"\n{title} ({rows.Count}):");
            foreach (var (query, expect, a, b) in rows)
            {
                var cos = sem.Similarities(query);
                var best = cos.Count > 0 ? cos.Aggregate((x, y) => y.Value > x.Value ? y : x) : new KeyValuePair<string, double>("-", 0);
                Console.WriteLine($"  «{query}»  ожидалось: {expect ?? "-"}");
                Console.WriteLine($"      без модели: {a.Kind} {a.First ?? "-"} ({ChatEngine.Fmt(a.Score)})   с моделью: {b.Kind} {b.First ?? "-"} ({ChatEngine.Fmt(b.Score)})" +
                                  $"   самый похожий по смыслу: {best.Key} ({ChatEngine.Fmt(best.Value)})");
            }
        }

        Show("Модель помогла", better);
        Show("Модель помешала — стоит поднять low или снизить weight", worse);
        Show("Изменился тип ответа, но оба правильные", changed);
        return 0;
    }

    // ---------- Выгрузка для настройки ----------

    /// <summary>
    /// Сохраняет для каждого контрольного запроса оценки по ключам и похожести по смыслу для ВСЕХ вопросов.
    /// Формат тот же, что у Python-версии: по файлу подбираются mode, zLow, zHigh, weight и combine.
    /// </summary>
    public static int SemanticDump(string contentDir, string? morphology)
    {
        var engine = LoadWithModel(contentDir, morphology, "Выгрузка");
        if (engine == null)
            return 1;
        var sem = engine.Semantic!;
        var kb = engine.KnowledgeBase;
        var prefix = engine.Config.Scoring.PrefixMinLength;

        var records = new List<object>();
        int firstByCos = 0, inTop3 = 0, firstByKw = 0, withExpect = 0;
        var rightCos = new List<double>();
        var wrongCos = new List<double>();
        var rightZ = new List<double>();
        foreach (var (query, expect, expectKind, source) in Cases(contentDir, engine))
        {
            var lemmas = engine.Text.Lemmatize(query);
            var keyword = kb.Questions.Where(q => q.IsActive)
                .ToDictionary(q => q.Id, q => Math.Round(Scorer.Evaluate(q.Keywords, lemmas, prefix, engine.Fuzzy).Score, 4));
            var cosine = sem.Similarities(query).ToDictionary(x => x.Key, x => Math.Round(x.Value, 4));
            records.Add(new { q = query, expect, expectKind, source, lemmas, keyword, cosine });

            if (expect == null || cosine.Count == 0)
                continue;
            withExpect++;
            var ranked = cosine.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();
            if (ranked[0] == expect) firstByCos++;
            if (ranked.Take(3).Contains(expect)) inTop3++;
            var topKeyword = keyword.Aggregate((x, y) => y.Value > x.Value ? y : x);
            if (topKeyword.Key == expect && keyword[expect] > 0) firstByKw++;
            rightCos.Add(cosine.GetValueOrDefault(expect));
            wrongCos.Add(cosine.Where(x => x.Key != expect).Max(x => x.Value));
            rightZ.Add(SemanticIndex.ZScores(cosine).GetValueOrDefault(expect));
        }

        var path = Path.Combine(Directory.GetCurrentDirectory(), DumpFile);
        var data = new
        {
            model = sem.Name,
            morphology = engine.Morphology,
            fuzzy = engine.Fuzzy?.Name,
            settings = new
            {
                mode = sem.Mode, zLow = sem.ZLow, zHigh = sem.ZHigh, topK = sem.TopK,
                low = sem.Low, high = sem.High, weight = sem.Weight, combine = engine.Config.Semantic.Combine
            },
            titles = kb.Questions.ToDictionary(q => q.Id, q => q.Title),
            cases = records
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

        static double Avg(List<double> values) => values.Count > 0 ? values.Average() : 0.0;

        Console.WriteLine($"Модель: {sem.Name}; запросов с ожидаемым ответом: {withExpect}");
        Console.WriteLine($"  правильный вопрос первый по смыслу:  {firstByCos} из {withExpect}");
        Console.WriteLine($"  правильный вопрос в тройке по смыслу: {inTop3} из {withExpect}");
        Console.WriteLine($"  правильный вопрос первый по ключам:  {firstByKw} из {withExpect}");
        Console.WriteLine($"  средняя похожесть: правильного {ChatEngine.Fmt(Avg(rightCos))}, лучшего неправильного {ChatEngine.Fmt(Avg(wrongCos))}");
        Console.WriteLine($"  средняя z-оценка правильного: {ChatEngine.Fmt(Avg(rightZ))}");
        Console.WriteLine($"Файл сохранён: {path}");
        Console.WriteLine("Пришлите этот файл — по нему подбираются настройки поиска по смыслу.");
        return 0;
    }
}
