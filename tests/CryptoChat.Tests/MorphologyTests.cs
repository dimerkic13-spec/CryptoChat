using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Text;
using CryptoChat.Cli;

namespace CryptoChat.Tests;

/// <summary>Подставная морфология: работает без словарей и считает обращения.</summary>
internal sealed class FakeLemmatizer : ILemmatizer
{
    private static readonly Dictionary<string, string> Forms = new()
    {
        ["лицензию"] = "лицензия", ["лицензии"] = "лицензия", ["лицензия"] = "лицензия",
        ["купить"] = "купить", ["купил"] = "купить", ["куплю"] = "купить", ["купили"] = "купить",
        ["оплата"] = "оплата", ["оплату"] = "оплата", ["кластер"] = "кластер", ["кластера"] = "кластер",
        ["узел"] = "узел", ["вернуть"] = "вернуть", ["возврат"] = "возврат",
        ["присоединились"] = "присоединиться", ["присоединиться"] = "присоединиться",
    };

    public int Calls { get; private set; }
    public string Name => "pymorphy3";

    public string Lemmatize(string word)
    {
        Calls++;
        return Forms.GetValueOrDefault(word, word);
    }
}

public class MorphSynonymsAndValidationTests
{
    [Fact]
    public void SameLemmaInsideOneGroup_IsAllowed()
    {
        var synonyms = new SynonymMap(new[] { new[] { "купить", "купил", "куплю", "покупка" } }, new FakeLemmatizer());
        Assert.Equal("купить", synonyms.MapLemma("покупка"));
    }

    [Fact]
    public void SameLemmaInTwoGroups_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new SynonymMap(new[] { new[] { "купить", "купил" }, new[] { "приобрести", "куплю" } }, new FakeLemmatizer()));
    }

    [Fact]
    public void DuplicateKeywordForms_AreOnlyWarning_AndMerged()
    {
        var content = TestContent.Valid();
        content.Questions[0].Keywords = TestContent.Kw(("купить", 5), ("купил", 2), ("лицензия", 3), ("оплата", 1));
        var lemmatizer = new FakeLemmatizer();

        var issues = ContentValidator.Validate(content, lemmatizer: lemmatizer);
        Assert.Contains(issues, i => i.Code == "V008" && i.Severity == Severity.Warning);
        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);

        var kb = KnowledgeBase.Build(content, lemmatizer);
        var words = kb.Questions[0].Keywords.ToDictionary(k => k.Word, k => k.Weight);
        Assert.Equal(new Dictionary<string, double> { ["купить"] = 5, ["лицензия"] = 3, ["оплата"] = 1 }, words);
    }

    [Fact]
    public void DuplicateKeywordWithStemmer_IsStillError()
    {
        var content = TestContent.Valid();
        content.Questions[0].Keywords[2].Word = "лицензию";
        var issues = ContentValidator.Validate(content);
        Assert.Contains(issues, i => i.Code == "V008" && i.Severity == Severity.Error);
    }

    [Fact]
    public void Pipeline_UsesMorphology()
    {
        var processor = new TextProcessor(lemmatizer: new FakeLemmatizer());
        Assert.Equal(new[] { "купить", "лицензия" }, processor.Lemmatize("как купил лицензию"));
    }

    [Fact]
    public void Engine_AnswersWithMorphology()
    {
        var engine = new ChatEngine(KnowledgeBase.Build(TestContent.Valid(), new FakeLemmatizer()), new AppConfig());
        var reply = engine.Ask("купили лицензию");
        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("1.1.1", reply.Question?.Id);
        Assert.Equal("pymorphy3", engine.Morphology);
    }
}

public class ModeSelectionTests
{
    private static ILemmatizer Missing() => throw new MorphologyUnavailableException("нет словарей");

    [Fact]
    public void StemmerMode()
    {
        var (lemmatizer, note) = Lemmatizers.Create("stemmer");
        Assert.IsType<StemmerLemmatizer>(lemmatizer);
        Assert.Null(note);
    }

    [Fact]
    public void UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => Lemmatizers.Create("magic"));
    }

    [Fact]
    public void Auto_FallsBackToStemmer_WithoutDictionaries()
    {
        var (lemmatizer, note) = Lemmatizers.Create("auto", Missing);
        Assert.IsType<StemmerLemmatizer>(lemmatizer);
        Assert.Contains("не найдены", note);
        Assert.Throws<MorphologyUnavailableException>(() => Lemmatizers.Create("pymorphy3", Missing));
    }

    [Fact]
    public void MissingDictionaryFolder_ExplainsWhatToDo()
    {
        var ex = Assert.Throws<MorphologyUnavailableException>(() =>
            MorphLemmatizer.Load(Path.Combine(Path.GetTempPath(), "нет-такой-папки")));
        Assert.Contains(MorphLemmatizer.DictionaryFolder, ex.Message);
    }
}

/// <summary>Настоящие словари pymorphy3 из папки dicts/.</summary>
public class RealMorphologyTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();
    private static MorphLemmatizer Morph() => MorphLemmatizer.Load();

    [Theory]
    [InlineData("лицензию", "лицензия")]
    [InlineData("купили", "купить")]
    [InlineData("рутокена", "рутокен")]
    [InlineData("присоединились", "присоединиться")]
    [InlineData("счёта", "счёт")] // ё в результате заменяется на е — см. следующий тест
    public void NormalForms(string word, string expected)
    {
        Assert.Equal(Normalizer.Normalize(expected), Morph().Lemmatize(word));
    }

    [Theory]
    [InlineData("псевдокошки", "псевдокошка")]   // известная приставка
    [InlineData("бутявкать", "бутявкать")]       // предсказание по окончанию
    [InlineData("интернет-магазина", "интернет-магазин")]
    [InlineData("смотри-ка", "смотреть-ка")]
    public void UnknownAndHyphenatedWords(string word, string expected)
    {
        Assert.Equal(expected, Morph().Lemmatize(word));
    }

    [Fact]
    public void WordsWithoutCyrillic_AreKept()
    {
        Assert.Equal("ngate", Morph().Lemmatize("ngate"));
        Assert.Equal("5.0", Morph().Lemmatize("5.0"));
    }

    [Fact]
    public void RealContent_LoadsWithMorphology()
    {
        var engine = ChatEngine.Create(ContentDir, "pymorphy3");
        Assert.Equal("pymorphy3", engine.Morphology);
        Assert.Equal(38, engine.KnowledgeBase.Questions.Count);
    }

    [Fact]
    public void Auto_ChoosesMorphology_WhenDictionariesArePresent()
    {
        var (lemmatizer, note) = Lemmatizers.Create("auto");
        Assert.IsType<MorphLemmatizer>(lemmatizer);
        Assert.Null(note);
    }

    /// <summary>Если тест падает, запустите CryptoChat --compare: по выводу подбираются ключевые слова под морфологию.</summary>
    [Fact]
    public void Acceptance_WithMorphology()
    {
        var (failures, _, _) = SelfTest.CheckAcceptance(ChatEngine.Create(ContentDir, "pymorphy3"), ContentDir);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>С морфологией «дней» → «день», а «день» — начало слова «деньги». Группа синонимов «средства/деньги» это исправляет.</summary>
    [Fact]
    public void Days_DoNotMatchMoney()
    {
        var reply = ChatEngine.Create(ContentDir, "pymorphy3").Ask("сколько дней работает без лицензии");
        Assert.Equal("1.1.2.2", SelfTest.FirstQuestionId(reply));
    }
}
