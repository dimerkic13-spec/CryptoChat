namespace CryptoChat.Core.Knowledge;

// «Сырые» данные ровно в том виде, как они записаны в JSON-файлах.
// Сначала их проверяет валидатор, и только потом из них строится KnowledgeBase.

public sealed class RawKeyword
{
    public string Word { get; set; } = "";
    public double Weight { get; set; }
}

public sealed class RawCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Description { get; set; }
    public string? Contact { get; set; }
    public List<RawKeyword> Keywords { get; set; } = new();
    public List<RawCategory> Children { get; set; } = new();
}

public sealed class RawSource
{
    public string Url { get; set; } = "";
    public string? Anchor { get; set; }
    public string? Revision { get; set; }
}

public sealed class RawQuestion
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Answer { get; set; } = "";
    public RawSource? Source { get; set; } = new();
    public string Status { get; set; } = "active";
    public List<RawKeyword> Keywords { get; set; } = new();

    /// <summary>Другие формулировки вопроса — для поиска по смыслу (необязательно).</summary>
    public List<string> Examples { get; set; } = new();
}

public sealed class RawTaxonomy
{
    public List<RawCategory> Categories { get; set; } = new();
}

public sealed class RawSynonyms
{
    public List<List<string>> Groups { get; set; } = new();
}

/// <summary>Всё содержимое каталога content/ в сыром виде.</summary>
public sealed class ContentSet
{
    public RawTaxonomy Taxonomy { get; set; } = new();
    public RawSynonyms Synonyms { get; set; } = new();
    public List<RawQuestion> Questions { get; set; } = new();
}
