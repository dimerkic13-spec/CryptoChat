using CryptoChat.Core.Knowledge;
using Xunit;

namespace CryptoChat.Tests;

public class KnowledgeLoaderTests
{
    /// <summary>Временный каталог с файлами данных; удаляется после теста.</summary>
    private sealed class TempContent : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cryptochat-" + Guid.NewGuid().ToString("N"));

        public TempContent() => Directory.CreateDirectory(Root);

        public void Write(string relativePath, string text)
        {
            var full = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { /* не страшно */ }
        }
    }

    private const string Taxonomy = """
        { "categories": [
            { "id": "1", "name": "Коммерческие", "kind": "topic",
              "children": [ { "id": "1.1", "name": "Лицензии", "kind": "subtopic" } ] }
        ] }
        """;

    private const string GoodQuestions = """
        [
          { "id": "1.1.1", "category": "1.1", "title": "Как купить лицензию?", "answer": "Ответ.",
            "source": { "url": "https://cryptopro.ru/faq/how-to-buy", "anchor": "a", "revision": "2026-10-09" },
            "keywords": [ { "word": "купить", "weight": 5 }, { "word": "лицензия", "weight": 3 }, { "word": "оплата", "weight": 1 } ] }
        ]
        """;

    [Fact]
    public void Load_ReadsAndStemsKeywords()
    {
        using var dir = new TempContent();
        dir.Write("taxonomy.json", Taxonomy);
        dir.Write("questions/a.json", GoodQuestions);

        var kb = KnowledgeLoader.Load(dir.Root);

        Assert.Single(kb.Roots);
        Assert.Single(kb.Questions);
        Assert.Equal(new[] { "куп", "лицензи", "оплат" }, kb.Questions[0].Keywords.Select(k => k.Word).ToArray());
        Assert.Single(kb.QuestionsUnder("1"));
        Assert.Empty(kb.QuestionsUnder("2"));
    }

    [Fact]
    public void Load_AppliesSynonymsToKeywords()
    {
        using var dir = new TempContent();
        dir.Write("taxonomy.json", Taxonomy);
        dir.Write("synonyms.json", """{ "groups": [ ["вернуть", "возврат"] ] }""");
        dir.Write("questions/a.json", """
            [ { "id": "1.1.1", "category": "1.1", "title": "Как вернуть лицензию?", "answer": "Ответ.",
                "source": { "url": "https://cryptopro.ru/faq/how-to-buy" },
                "keywords": [ { "word": "возврат", "weight": 5 }, { "word": "деньги", "weight": 3 }, { "word": "лицензия", "weight": 1 } ] } ]
            """);

        var kb = KnowledgeLoader.Load(dir.Root);

        Assert.Contains(kb.Questions[0].Keywords, k => k.Word == "верн");
    }

    [Fact]
    public void Load_MissingDirectory_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "cryptochat-no-such-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ContentException>(() => KnowledgeLoader.Load(missing));
    }

    [Fact]
    public void Load_MissingTaxonomy_Throws()
    {
        using var dir = new TempContent();
        var ex = Assert.Throws<ContentException>(() => KnowledgeLoader.Load(dir.Root));
        Assert.Contains("taxonomy.json", ex.Message);
    }

    [Fact]
    public void Load_BrokenJson_ThrowsWithFileName()
    {
        using var dir = new TempContent();
        dir.Write("taxonomy.json", "{ это не JSON");
        var ex = Assert.Throws<ContentException>(() => KnowledgeLoader.Load(dir.Root));
        Assert.Contains("taxonomy.json", ex.Message);
    }

    [Fact]
    public void Load_InvalidContent_ThrowsWithErrorCodes()
    {
        using var dir = new TempContent();
        dir.Write("taxonomy.json", Taxonomy);
        dir.Write("questions/a.json", GoodQuestions.Replace("\"Ответ.\"", "\"\""));

        var ex = Assert.Throws<ContentException>(() => KnowledgeLoader.Load(dir.Root));
        Assert.Contains("V004", ex.Message);
    }
}
