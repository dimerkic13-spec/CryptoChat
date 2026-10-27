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

    private KnowledgeBase(
        IReadOnlyList<Category> roots,
        IReadOnlyDictionary<string, Category> categories,
        IReadOnlyList<Question> questions,
        SynonymMap synonyms)
    {
        Roots = roots;
        Categories = categories;
        Questions = questions;
        Synonyms = synonyms;
    }

    /// <summary>Вопросы выбранной категории и всех её вложенных категорий.</summary>
    public IEnumerable<Question> QuestionsUnder(string categoryId) =>
        Questions.Where(q => q.CategoryId == categoryId
                             || q.CategoryId.StartsWith(categoryId + ".", StringComparison.Ordinal));

    /// <summary>Строит базу из проверенных данных. Ключевые слова стеммятся здесь, один раз.</summary>
    public static KnowledgeBase Build(ContentSet content)
    {
        var synonyms = new SynonymMap(content.Synonyms.Groups);
        var flat = new Dictionary<string, Category>();

        IReadOnlyList<Keyword> ToKeywords(IEnumerable<RawKeyword> raws) =>
            raws.Select(k => new Keyword(
                    synonyms.MapLemma(Stemmer.Stem(Normalizer.Normalize(k.Word.Trim()))),
                    k.Weight))
                .ToList();

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
                q.Source!.Url, q.Source.Anchor, q.Source.Revision,
                !string.Equals(q.Status, "draft", StringComparison.OrdinalIgnoreCase),
                ToKeywords(q.Keywords)))
            .ToList();

        return new KnowledgeBase(roots, flat, questions, synonyms);
    }
}
