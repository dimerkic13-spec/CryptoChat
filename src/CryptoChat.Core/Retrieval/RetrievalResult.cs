using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

/// <summary>Кандидат: вопрос, итоговая оценка, оценка только по ключам и похожесть по смыслу (если поиск по смыслу включён).</summary>
public sealed record Candidate(Question Question, double Score, double KeywordScore = 0, double? Cosine = null);

/// <summary>Итог поиска: леммы запроса, найденная область (или null) и кандидаты по убыванию score.</summary>
public sealed record RetrievalResult(
    IReadOnlyList<string> Lemmas,
    Category? Scope,
    double ScopeScore,
    IReadOnlyList<Candidate> Candidates)
{
    private static readonly IReadOnlyDictionary<string, double> None = new Dictionary<string, double>();

    /// <summary>Id вопроса → смысловая оценка (с весом).</summary>
    public IReadOnlyDictionary<string, double> Semantic { get; init; } = None;

    /// <summary>Id вопроса → похожесть по смыслу (для запасного меню).</summary>
    public IReadOnlyDictionary<string, double> Cosines { get; init; } = None;
}
