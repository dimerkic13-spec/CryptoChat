namespace CryptoChat.Core.Models;

public enum ReplyKind
{
    Answer,
    Clarify,
    ClarifyProduct,
    ClarifyBranch,
    Escalate,
    NotUnderstood,
    Feedback,
    Command,
    Exit
}

/// <summary>
/// Один пункт меню: вопрос, продукт или ветка дерева (тема, блок).
/// Browse — пункт «посмотреть по списку»: показать вопросы раздела Branch (или список тем, если Branch нет)
/// без нового поиска — так пользователь доходит до нужного вопроса, даже если меню не угадало.
/// </summary>
public sealed record ChatOption(
    int Number,
    string Label,
    double Score,
    Question? Question,
    Category? Product,
    Category? Branch = null,
    bool Browse = false);

/// <summary>Реакция бота на ввод пользователя. Консоль только печатает Text.</summary>
public sealed record ChatReply(
    ReplyKind Kind,
    string Text,
    IReadOnlyList<ChatOption> Options,
    string? Debug = null,
    Question? Question = null,
    double Score = 0);

/// <summary>Отзыв пользователя об ответе («да» — помог, «нет» — не помог).</summary>
public sealed record FeedbackEntry(DateTime Time, string Query, string QuestionId, bool Helpful);
