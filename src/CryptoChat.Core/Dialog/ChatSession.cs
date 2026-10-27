using CryptoChat.Core.Models;

namespace CryptoChat.Core.Dialog;

/// <summary>
/// Меню, на выбор из которого бот сейчас ждёт ответа.
/// FeedbackQuestionId заполнен, если бот только что выдал ответ и ждёт отзыв «да» / «нет».
/// </summary>
public sealed record PendingChoice(
    ReplyKind Kind,
    IReadOnlyList<ChatOption> Options,
    IReadOnlyList<string> Lemmas,
    string? FeedbackQuestionId = null,
    string? Query = null);

/// <summary>Состояние диалога: ждём ли выбор из меню и сколько шагов уточнения уже прошло.</summary>
public sealed class ChatSession
{
    public PendingChoice? Pending { get; private set; }

    /// <summary>Сколько раз подряд бот уточнял, не дав ответа.</summary>
    public int Steps { get; private set; }

    public void Wait(PendingChoice choice) => Pending = choice;

    /// <summary>Сбрасывает только ожидание выбора; счётчик шагов уточнения сохраняется.</summary>
    public void Reset() => Pending = null;

    public void NextStep() => Steps++;

    /// <summary>Сбрасывает всё: ожидание выбора и счётчик шагов.</summary>
    public void ResetAll()
    {
        Pending = null;
        Steps = 0;
    }

    public void ResetSteps() => Steps = 0;
}
