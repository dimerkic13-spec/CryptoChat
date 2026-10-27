using CryptoChat.Core.Models;

namespace CryptoChat.Core.Knowledge;

public static class KnowledgeBaseExtensions
{
    public static Category? ParentOf(this KnowledgeBase kb, Category category) =>
        category.ParentId != null && kb.Categories.TryGetValue(category.ParentId, out var parent) ? parent : null;

    /// <summary>Идёт вверх по дереву от категории и возвращает первую, подходящую под условие.</summary>
    public static Category? NearestAncestorOrSelf(this KnowledgeBase kb, string categoryId, Func<Category, bool> predicate)
    {
        if (!kb.Categories.TryGetValue(categoryId, out var start))
            return null;

        for (Category? current = start; current != null; current = kb.ParentOf(current))
        {
            if (predicate(current))
                return current;
        }
        return null;
    }
}
