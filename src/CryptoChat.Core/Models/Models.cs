namespace CryptoChat.Core.Models;

/// <summary>Ключевое слово: основа слова (после стемминга) и вес.</summary>
public sealed record Keyword(string Word, double Weight);

/// <summary>Категория дерева: тема, продукт, подтема, блок или подблок.</summary>
public sealed record Category(
    string Id,
    string? ParentId,
    string Name,
    string Kind,
    string? Contact,
    IReadOnlyList<Keyword> Keywords,
    IReadOnlyList<Category> Children);

/// <summary>Вопрос с готовым ответом.</summary>
public sealed record Question(
    string Id,
    string CategoryId,
    string Title,
    string Answer,
    string SourceUrl,
    string? Anchor,
    string? Revision,
    bool IsActive,
    IReadOnlyList<Keyword> Keywords);
