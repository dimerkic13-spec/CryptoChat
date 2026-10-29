using CryptoChat.Core.Models;
using CryptoChat.Core.Text;

namespace CryptoChat.Core.Knowledge;

/// <summary>База знаний в памяти: дерево категорий, вопросы и синонимы.</summary>
public sealed class KnowledgeBase
{
    public IReadOnlyList<Category> Roots { get; }
    public IReadOnlyDictionary<string, Category> Categories { get; }
    public IReadOnlyList<Question> Questions { get; }
    public SynonymMap Synonyms { get; }

    /// <summary>Как слова приводятся к основе. Тот же лемматизатор нужен для обработки запросов.</summary>
    public ILemmatizer Lemmatizer { get; }

    private KnowledgeBase(
        IReadOnlyList<Category> roots,
        IReadOnlyDictionary<string, Category> categories,
        IReadOnlyList<Question> questions,
        SynonymMap synonyms,
        ILemmatizer lemmatizer)
    {
        Roots = roots;
        Categories = categories;
        Questions = questions;
        Synonyms = synonyms;
        Lemmatizer = lemmatizer;
    }

    /// <summary>Вопросы выбранной категории и всех её вложенных категорий.</summary>
    public IEnumerable<Question> QuestionsUnder(string categoryId) =>
        Questions.Where(q => q.CategoryId == categoryId
                             || q.CategoryId.StartsWith(categoryId + ".", StringComparison.Ordinal));

    /// <summary>Строит базу из проверенных данных. Ключевые слова приводятся к основе здесь, один раз.</summary>
    public static KnowledgeBase Build(ContentSet content, ILemmatizer? lemmatizer = null)
    {
        lemmatizer ??= StemmerLemmatizer.Instance;
        var synonyms = new SynonymMap(content.Synonyms.Groups, lemmatizer);
        var flat = new Dictionary<string, Category>();

        // Если два ключа дали одну основу (с морфологией: «купил» и «купить»),
        // остаётся один ключ с большим весом.
        IReadOnlyList<Keyword> ToKeywords(IEnumerable<RawKeyword> raws)
        {
            var result = new List<Keyword>();
            var positions = new Dictionary<string, int>();
            foreach (var k in raws)
            {
                var word = synonyms.MapLemma(lemmatizer.Lemmatize(Normalizer.Normalize(k.Word.Trim())));
                if (positions.TryGetValue(word, out var index))
                {
                    result[index] = result[index] with { Weight = Math.Max(result[index].Weight, k.Weight) };
                    continue;
                }
                positions[word] = result.Count;
                result.Add(new Keyword(word, k.Weight));
            }
            return result;
        }

        Category Make(RawCategory raw, string? parentId)
        {
            var children = raw.Children.Select(child => Make(child, raw.Id)).ToList();
            var category = new Category(raw.Id, parentId, raw.Name, raw.Kind, raw.Contact,
                ToKeywords(raw.Keywords), children);
            flat[category.Id] = category;
            return category;
        }

        var roots = content.Taxonomy.Categories.Select(c => Make(c, null)).ToList();

        var questions = content.Questions
            .Select(q => new Question(
                q.Id, q.Category, q.Title, q.Answer,
                q.Source?.Url ?? "", q.Source?.Anchor, q.Source?.Revision,
                !string.Equals(q.Status, "draft", StringComparison.OrdinalIgnoreCase),
                ToKeywords(q.Keywords),
                q.Examples.Where(e => !string.IsNullOrWhiteSpace(e)).ToList()))
            .ToList();

        return new KnowledgeBase(roots, flat, questions, synonyms, lemmatizer);
    }
}
