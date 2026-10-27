using CryptoChat.Core.Knowledge;

namespace CryptoChat.Tests;

/// <summary>Маленькая корректная база для тестов. Каждый тест портит одно место.</summary>
internal static class TestContent
{
    public static List<RawKeyword> Kw(params (string Word, double Weight)[] items) =>
        items.Select(i => new RawKeyword { Word = i.Word, Weight = i.Weight }).ToList();

    public static RawQuestion Q(string id, string category, string title, params (string Word, double Weight)[] keywords) =>
        new()
        {
            Id = id,
            Category = category,
            Title = title,
            Answer = "Текст ответа.",
            Source = new RawSource { Url = "https://cryptopro.ru/faq/test", Anchor = "test", Revision = "2026-10-09" },
            Keywords = Kw(keywords)
        };

    public static ContentSet Valid() => new()
    {
        Taxonomy = new RawTaxonomy
        {
            Categories = new List<RawCategory>
            {
                new()
                {
                    Id = "1", Name = "Коммерческие", Kind = "topic",
                    Children = new List<RawCategory>
                    {
                        new() { Id = "1.1", Name = "Лицензии", Kind = "subtopic" }
                    }
                },
                new()
                {
                    Id = "2", Name = "Продукты", Kind = "topic",
                    Children = new List<RawCategory>
                    {
                        new()
                        {
                            Id = "2.1", Name = "NGate", Kind = "product", Contact = "ngate@cryptopro.ru",
                            Keywords = Kw(("ngate", 6), ("кластер", 3))
                        }
                    }
                }
            }
        },
        Synonyms = new RawSynonyms
        {
            Groups = new List<List<string>> { new() { "вернуть", "возврат" } }
        },
        Questions = new List<RawQuestion>
        {
            Q("1.1.1", "1.1", "Как купить лицензию?", ("купить", 5), ("лицензия", 3), ("оплата", 1)),
            Q("2.1.1", "2.1", "Что такое кластер NGate?", ("ngate", 6), ("кластер", 3), ("узел", 1))
        }
    };
}
