namespace CryptoChat.Core.Models;

public enum ReplyKind
{
    Answer,
    Clarify,
    ClarifyProduct,
    Escalate,
    NotUnderstood,
    Command,
    Exit
}

/// <summary>Один пункт меню уточнения: вопрос или продукт.</summary>
public sealed record ChatOption(int Number, string Label, double Score, Question? Question, Category? Product);

/// <summary>Реакция бота на ввод пользователя. Консоль только печатает Text.</summary>
public sealed record ChatReply(
    ReplyKind Kind,
    string Text,
    IReadOnlyList<ChatOption> Options,
    string? Debug = null,
    Question? Question = null,
    double Score = 0);
