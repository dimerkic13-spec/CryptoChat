using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

public sealed record Candidate(Question Question, double Score);

/// <summary>Итог поиска: леммы запроса, найденная область (или null) и кандидаты по убыванию score.</summary>
public sealed record RetrievalResult(
    IReadOnlyList<string> Lemmas,
    Category? Scope,
    double ScopeScore,
    IReadOnlyList<Candidate> Candidates);
