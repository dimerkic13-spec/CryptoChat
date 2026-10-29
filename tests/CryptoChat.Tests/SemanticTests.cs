using System.Text.Json;
using CryptoChat.Cli;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using CryptoChat.Core.Semantic;

namespace CryptoChat.Tests;

/// <summary>Ведёт себя как модель: фразы с любым словом из группы получают одно и то же направление.</summary>
internal sealed class FakeEncoder : IEncoder
{
    private static readonly string[][] Concepts =
    {
        new[] { "кабинет", "аккаунт", "зайти", "войти", "логин" },
        new[] { "купить", "покупк", "приобрест", "лицензи" },
        new[] { "кластер", "ngate", "узел", "отказоустойчив" },
        new[] { "пароль" },
        new[] { "сбой", "ошибк" },
    };

    public int Calls { get; private set; }
    public List<string> Seen { get; } = new();
    public string Name => "поддельная модель";

    public IReadOnlyList<double[]> Encode(IReadOnlyList<string> texts)
    {
        Calls++;
        Seen.AddRange(texts);
        return texts.Select(text =>
        {
            var low = text.ToLowerInvariant();
            return Concepts.Select(group => group.Any(low.Contains) ? 1.0 : 0.0)
                .Append(0.05) // чтобы вектор не был нулевым
                .ToArray();
        }).ToList();
    }
}

internal static class SemanticTestContent
{
    public static ContentSet WithCabinet()
    {
        var content = TestContent.Valid();
        content.Questions.Add(TestContent.Q("1.1.2", "1.1", "Забыл логин от личного кабинета",
            ("кабинет", 6), ("логин", 4), ("забыл", 2)));
        return content;
    }
}

public class SemanticIndexTests
{
    private readonly KnowledgeBase _kb = KnowledgeBase.Build(SemanticTestContent.WithCabinet());

    [Fact]
    public void ScoreMapping()
    {
        var index = new SemanticIndex(new FakeEncoder(), _kb.Questions, low: 0.5, high: 0.9, weight: 0.8);
        Assert.Equal(0.0, index.ToScore(0.5));
        Assert.Equal(0.4, index.ToScore(0.7), 9);
        Assert.Equal(0.8, index.ToScore(0.95), 9);
    }

    [Fact]
    public void Paraphrase_WithoutCommonWords_IsClose()
    {
        var cos = new SemanticIndex(new FakeEncoder(), _kb.Questions).Similarities("не могу зайти в аккаунт");
        Assert.Equal("1.1.2", cos.MaxBy(x => x.Value).Key);
        Assert.True(cos["1.1.2"] > 0.9);
    }

    [Fact]
    public void Examples_AreIndexed()
    {
        var content = SemanticTestContent.WithCabinet();
        content.Questions[1].Examples = new List<string> { "отказоустойчивость нескольких серверов", "  " };
        var index = new SemanticIndex(new FakeEncoder(), KnowledgeBase.Build(content).Questions);
        Assert.Equal(4, index.Phrases); // 3 заголовка + 1 пример (пустой не считается)
    }

    [Fact]
    public void LastQuery_IsCached()
    {
        var encoder = new FakeEncoder();
        var index = new SemanticIndex(encoder, _kb.Questions);
        index.Similarities("зайти в аккаунт");
        index.Similarities("зайти в аккаунт");
        Assert.Equal(2, encoder.Calls); // 1 раз для вопросов + 1 раз для запроса
    }

    [Fact]
    public void Prefixes_AreAdded()
    {
        var encoder = new FakeEncoder();
        var index = new SemanticIndex(encoder, KnowledgeBase.Build(TestContent.Valid()).Questions,
            queryPrefix: "query: ", passagePrefix: "passage: ");
        index.Similarities("вопрос");
        Assert.All(encoder.Seen.SkipLast(1), t => Assert.StartsWith("passage: ", t));
        Assert.Equal("query: вопрос", encoder.Seen[^1]);
    }

    [Fact]
    public void PySum_CompensatesRounding()
    {
        // Как sum() в Python 3.12+: обычное сложение дало бы 0.0.
        Assert.Equal(1.0, SemanticIndex.PySum(new[] { 1e100, 1.0, -1e100 }));
        Assert.Equal(0.6, SemanticIndex.PySum(new[] { 0.1, 0.2, 0.3 }));
    }
}

public class RelativeModeTests
{
    /// <summary>Модель с заранее заданными векторами: похожесть запроса с вопросом i — values[i].</summary>
    private sealed class TableEncoder : IEncoder
    {
        private readonly double[] _values;
        public TableEncoder(double[] values) => _values = values;
        public string Name => "таблица";

        public IReadOnlyList<double[]> Encode(IReadOnlyList<string> texts) =>
            texts.Count == _values.Length
                ? texts.Select((_, i) => Enumerable.Range(0, texts.Count).Select(j => j == i ? 1.0 : 0.0).ToArray()).ToList()
                : new[] { _values.ToArray() };
    }

    private static SemanticIndex Index(double[] values, double zLow = 2.5, double zHigh = 4.5, int topK = 3)
    {
        var content = TestContent.Valid();
        for (int n = 0; n < values.Length - 2; n++)
            content.Questions.Add(TestContent.Q($"1.1.{n + 2}", "1.1", $"Вопрос {n}", ("слово", 1), ("ещё", 1), ("три", 1)));
        return new SemanticIndex(new TableEncoder(values), KnowledgeBase.Build(content).Questions,
            mode: "relative", weight: 0.5, zLow: zLow, zHigh: zHigh, topK: topK);
    }

    [Fact]
    public void FlatSimilarities_GiveNothing() =>
        Assert.Empty(Index(Enumerable.Repeat(0.8, 10).ToArray()).Scores("запрос"));

    [Fact]
    public void StandoutQuestion_GetsScore()
    {
        var scores = Index(Enumerable.Repeat(0.3, 9).Append(0.9).ToArray(), zLow: 1.0, zHigh: 2.0).Scores("запрос");
        Assert.Equal(0.5, Assert.Single(scores).Value, 9);
    }

    [Fact]
    public void OnlyTopK_Considered()
    {
        var values = Enumerable.Repeat(0.1, 6).Concat(Enumerable.Repeat(0.9, 4)).ToArray();
        Assert.Equal(2, Index(values, zLow: 0.5, zHigh: 1.0, topK: 2).Scores("запрос").Count);
    }

    [Fact]
    public void CombineModes()
    {
        Assert.Equal(0.5, Scorer.CombineScores(0.5, 0.3, "max"), 9);
        Assert.Equal(0.65, Scorer.CombineScores(0.5, 0.3, "or"), 9);
        Assert.Equal(0.3, Scorer.CombineScores(0.0, 0.3, "or"), 9);
    }
}

public class SemanticEngineTests
{
    private static ChatEngine Engine(double weight = 0.8)
    {
        var kb = KnowledgeBase.Build(SemanticTestContent.WithCabinet());
        var index = new SemanticIndex(new FakeEncoder(), kb.Questions, low: 0.5, high: 0.9, weight: weight);
        return new ChatEngine(kb, new AppConfig(), semantic: index);
    }

    [Fact]
    public void FindsQuestion_WithNoCommonWords()
    {
        var plain = new ChatEngine(KnowledgeBase.Build(SemanticTestContent.WithCabinet()), new AppConfig());
        Assert.Equal(ReplyKind.Escalate, plain.Ask("не могу зайти в аккаунт").Kind);

        var reply = Engine().Ask("не могу зайти в аккаунт");
        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("1.1.2", reply.Question?.Id);
    }

    [Fact]
    public void LowWeight_OnlySuggests()
    {
        var reply = Engine(weight: 0.5).Ask("не могу зайти в аккаунт");
        Assert.Equal(ReplyKind.Clarify, reply.Kind);
        Assert.Equal("1.1.2", reply.Options[0].Question?.Id);
    }

    [Fact]
    public void Keywords_StillWin_WhenStronger()
    {
        var reply = Engine().Ask("кластер ngate");
        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("2.1.1", reply.Question?.Id);
    }

    [Fact]
    public void Debug_ShowsSimilarity()
    {
        var engine = Engine();
        engine.DebugEnabled = true;
        var debug = engine.Ask("не могу зайти в аккаунт").Debug!;
        Assert.Contains("смысл: похожесть", debug);
        Assert.Contains("смысл: поддельная модель", debug);
    }
}

public class SemanticMenuFallbackTests
{
    private static ChatEngine Engine(bool menuFallback = true, double menuZ = 1.0)
    {
        var content = SemanticTestContent.WithCabinet();
        // несколько «посторонних» вопросов, чтобы было на фоне чего выделяться
        var titles = new[] { "Как получить счёт", "Как вернуть деньги", "Нужен договор", "Где акт сверки" };
        for (int n = 0; n < titles.Length; n++)
            content.Questions.Add(TestContent.Q($"1.1.{n + 3}", "1.1", titles[n], ("счет", 1), ("договор", 1), ("акт", 1)));
        var kb = KnowledgeBase.Build(content);
        var config = new AppConfig();
        config.Dialog.Guided = true;
        config.Semantic.MenuFallback = menuFallback;
        config.Semantic.MenuZ = menuZ;
        // weight маленький: сам по себе смысл не дотягивает до порога 0.3, работает только запасное меню
        var index = new SemanticIndex(new FakeEncoder(), kb.Questions, mode: "relative", zLow: 0.0, zHigh: 5.0, weight: 0.1);
        return new ChatEngine(kb, config, semantic: index);
    }

    [Fact]
    public void Menu_InsteadOfTopics()
    {
        var reply = Engine().Ask("не могу зайти в аккаунт");
        Assert.Equal(ReplyKind.Clarify, reply.Kind);
        Assert.Equal("1.1.2", reply.Options[0].Question?.Id);
        Assert.Contains("Возможно, вы имели в виду", reply.Text);
    }

    [Fact]
    public void ChoiceFromMenu_GivesAnswer()
    {
        var engine = Engine();
        engine.Ask("не могу зайти в аккаунт");
        var answer = engine.Ask("1");
        Assert.Equal(ReplyKind.Answer, answer.Kind);
        Assert.Equal("1.1.2", answer.Question?.Id);
    }

    [Fact]
    public void DisabledFallback_GoesToTopics() =>
        Assert.Equal(ReplyKind.ClarifyBranch, Engine(menuFallback: false).Ask("не могу зайти в аккаунт").Kind);

    [Fact]
    public void NothingStandsOut_GoesToTopics() =>
        Assert.Equal(ReplyKind.ClarifyBranch, Engine(menuZ: 10).Ask("не могу зайти в аккаунт").Kind);

    [Fact]
    public void Menu_CountsAsClarificationStep()
    {
        var engine = Engine();
        for (int i = 0; i < 4; i++)
            Assert.Equal(ReplyKind.Clarify, engine.Ask("не могу зайти в аккаунт").Kind);
        Assert.Equal(ReplyKind.Escalate, engine.Ask("не могу зайти в аккаунт").Kind);
    }
}

public class SemanticFactoryTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();

    [Fact]
    public void Disabled_GivesNothing() =>
        Assert.Equal((null, null), SemanticFactory.Create(new SemanticConfig { Enabled = false }, Array.Empty<Question>()));

    [Fact]
    public void Unavailable_GivesNote()
    {
        var (index, note) = SemanticFactory.Create(new SemanticConfig { Enabled = true }, Array.Empty<Question>(),
            encoderFactory: _ => throw new SemanticUnavailableException("нет модели"));
        Assert.Null(index);
        Assert.Equal("нет модели", note);
    }

    [Fact]
    public void ModelFolder_IsInsideCacheDir_RelativeToAppDir()
    {
        string? seen = null;
        var (index, _) = SemanticFactory.Create(new SemanticConfig { Enabled = true }, Array.Empty<Question>(), "/app",
            folder => { seen = folder; return new FakeEncoder(); });
        Assert.NotNull(index);
        Assert.Equal(Path.Combine("/app", "models", "onnx", "cointegrated--rubert-tiny2"), seen);
    }

    [Fact]
    public void MissingModel_ExplainsHowToExport()
    {
        var (index, note) = SemanticFactory.Create(new SemanticConfig { Enabled = true, CacheDir = Path.GetTempPath() },
            Array.Empty<Question>());
        Assert.Null(index);
        Assert.Contains("export_onnx.py", note);
        Assert.False(SemanticFactory.ModelExists(new SemanticConfig { CacheDir = Path.GetTempPath() }, null));
    }

    [Fact]
    public void RealConfig_HasCalibratedValues()
    {
        var s = AppConfig.Load(ContentDir).Semantic;
        Assert.True(s.Enabled);
        Assert.Equal("cointegrated/rubert-tiny2", s.Model);
        Assert.Equal(("relative", "or"), (s.Mode, s.Combine));
        Assert.Equal(0.3, s.Weight);
        Assert.Equal(0.0, s.ZLow);
        Assert.Equal(5.0, s.ZHigh);
        Assert.True(s.MenuFallback);
        Assert.Equal(1.5, s.MenuZ);
    }

    [Fact]
    public void BotWorks_WhenModelIsMissing()
    {
        var engine = ChatEngine.Create(ContentDir, "stemmer", appDir: Path.GetTempPath());
        Assert.Null(engine.Semantic);
        Assert.NotNull(engine.SemanticNote);
        Assert.Equal(ReplyKind.Answer, engine.Ask("Можно оплатить счёт на ИП банковской картой?").Kind);
    }
}

/// <summary>Маленькая модель ONNX (средний вектор токенов) из tests/data/fake-model: проверка ONNX Runtime и токенизатора.</summary>
public class OnnxEncoderTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "data", "fake-model");

    [Fact]
    public void Model_MatchesReferenceFromPython()
    {
        using var encoder = OnnxEncoder.Load(Folder);
        Assert.Equal("test/fake-model", encoder.Name);
        foreach (var reference in encoder.Info.Reference)
        {
            Assert.Equal(reference.Ids, encoder.Tokenize(reference.Text));
            var vector = encoder.Encode(new[] { reference.Text })[0];
            Assert.Equal(reference.Vector.Count, vector.Length);
            for (int i = 0; i < vector.Length; i++)
                Assert.Equal(reference.Vector[i], vector[i], 5);
        }
    }

    [Fact]
    public void Tokenizer_MatchesHuggingFace()
    {
        var tokenizer = WordPieceTokenizer.Load(Path.Combine(Folder, "tokenizer.json"));
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "tokenizer_cases.json")));
        foreach (var c in cases.RootElement.EnumerateArray())
        {
            var expected = c[1].EnumerateArray().Select(x => x.GetInt32()).ToList();
            Assert.Equal(expected, tokenizer.Encode(c[0].GetString()!));
        }
    }

    [Fact]
    public void Truncation_KeepsSpecialTokens()
    {
        var tokenizer = WordPieceTokenizer.Load(Path.Combine(Folder, "tokenizer.json"));
        var ids = tokenizer.Encode(string.Concat(Enumerable.Repeat("лицензия ", 100)), maxLength: 8);
        Assert.Equal(8, ids.Count);
        Assert.Equal(tokenizer.Encode("")[0], ids[0]);  // [CLS]
        Assert.Equal(tokenizer.Encode("")[^1], ids[^1]); // [SEP]
    }

    [Fact]
    public void WholeBot_WorksWithOnnxModel()
    {
        using var encoder = OnnxEncoder.Load(Folder);
        var kb = KnowledgeLoader.Load(TestSupport.FindContentDir());
        var config = AppConfig.Load(TestSupport.FindContentDir());
        var index = new SemanticIndex(encoder, kb.Questions, mode: "relative", weight: 0.3, zLow: 0, zHigh: 5);
        Assert.Equal(152, index.Phrases); // 38 заголовков + 114 примеров
        var reply = new ChatEngine(kb, config, semantic: index).Ask("Можно оплатить счёт на ИП банковской картой?");
        Assert.Equal(ReplyKind.Answer, reply.Kind);
    }
}

/// <summary>
/// Похожести настоящей модели rubert-tiny2 (выгрузка с компьютера пользователя, tests/data/semantic_calibration.json)
/// и настройки из config.json. Если тест упал после правки базы или кода — поиск по смыслу стал работать хуже,
/// чем при калибровке Python-версии.
/// </summary>
public class CalibrationRegressionTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();

    private sealed class Table : IEncoder
    {
        public string Name => "rubert-tiny2 (записанные похожести)";
        public IReadOnlyList<double[]> Encode(IReadOnlyList<string> texts) => texts.Select(_ => new[] { 1.0 }).ToList();
    }

    private sealed class RecordedIndex : SemanticIndex
    {
        private readonly Dictionary<string, Dictionary<string, double>> _cosines;

        public RecordedIndex(Dictionary<string, Dictionary<string, double>> cosines, IEnumerable<Question> questions, SemanticConfig c)
            : base(new Table(), questions, c.Low, c.High, c.Weight, null, c.Mode, c.ZLow, c.ZHigh, c.TopK) => _cosines = cosines;

        public override IReadOnlyDictionary<string, double> Similarities(string? query) =>
            _cosines.TryGetValue((query ?? "").Trim(), out var d) ? d : new Dictionary<string, double>();
    }

    private static ChatEngine Template()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "semantic_calibration.json")));
        var cosines = doc.RootElement.GetProperty("cosines").EnumerateObject().ToDictionary(
            q => q.Name, q => q.Value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetDouble()));
        var engine = ChatEngine.Create(ContentDir, "stemmer", semantic: false);
        engine.Config.Semantic.Enabled = true;
        var index = new RecordedIndex(cosines, engine.KnowledgeBase.Questions, engine.Config.Semantic);
        return new ChatEngine(engine.KnowledgeBase, engine.Config, semantic: index);
    }

    [Fact]
    public void Acceptance_StillPasses_WithModel()
    {
        var (failures, _, _) = SelfTest.CheckAcceptance(Template(), ContentDir);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Paraphrases_Improve()
    {
        var template = Template();
        int first = 0, inMenu = 0;
        foreach (var c in SelfTest.LoadCases(ContentDir, "paraphrases.json"))
        {
            var reply = SelfTest.Fresh(template).Ask(c.Q);
            var ids = reply.Kind == ReplyKind.Answer
                ? new List<string> { reply.Question!.Id }
                : reply.Options.Where(o => o.Question != null).Select(o => o.Question!.Id).ToList();
            if (ids.Count > 0 && ids[0] == c.Expect) first++;
            if (ids.Contains(c.Expect!)) inMenu++;
        }
        Assert.True(first >= 6, $"первым: {first}");
        Assert.True(inMenu >= 8, $"в меню: {inMenu}");
    }
}
