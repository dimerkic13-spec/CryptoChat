using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Text;

namespace CryptoChat.Tests;

/// <summary>Общие помощники для тестов поиска и диалога.</summary>
internal static class TestSupport
{
    public static string FindContentDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "taxonomy.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Каталог content/ не найден выше папки с тестами.");
    }

    public static IReadOnlyList<string> Lemmas(KnowledgeBase kb, string text) =>
        new TextProcessor(synonyms: kb.Synonyms).Lemmatize(text);

    /// <summary>Тестовая база с двумя продуктами, у которых совпадает слово «лицензия».</summary>
    public static ContentSet TwoProducts() => new()
    {
        Taxonomy = new RawTaxonomy
        {
            Categories = new List<RawCategory>
            {
                new()
                {
                    Id = "2", Name = "Технические вопросы", Kind = "topic",
                    Children = new List<RawCategory>
                    {
                        new()
                        {
                            Id = "2.1", Name = "КриптоПро CSP", Kind = "product",
                            Keywords = TestContent.Kw(("csp", 6), ("лицензия", 3))
                        },
                        new()
                        {
                            Id = "2.2", Name = "КриптоПро NGate", Kind = "product", Contact = "ngate@cryptopro.ru",
                            Keywords = TestContent.Kw(("ngate", 6), ("лицензия", 3))
                        }
                    }
                }
            }
        },
        Questions = new List<RawQuestion>
        {
            TestContent.Q("2.1.1", "2.1", "Лицензия CSP?", ("лицензия", 5), ("csp", 3), ("ключ", 1)),
            TestContent.Q("2.2.1", "2.2", "Лицензия NGate?", ("лицензия", 5), ("ngate", 3), ("ключ", 1))
        }
    };
}
