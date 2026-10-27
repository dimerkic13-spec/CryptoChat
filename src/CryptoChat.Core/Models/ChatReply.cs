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

/// <summary>Один пункт меню: вопрос, продукт или ветка дерева (тема, блок).</summary>
public sealed record ChatOption(
    int Number,
    string Label,
    double Score,
    Question? Question,
    Category? Product,
    Category? Branch = null);

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
