using CryptoChat.Core.Models;

namespace CryptoChat.Core.Dialog;

/// <summary>Меню, на выбор из которого бот сейчас ждёт ответа.</summary>
public sealed record PendingChoice(ReplyKind Kind, IReadOnlyList<ChatOption> Options, IReadOnlyList<string> Lemmas);

/// <summary>Состояние диалога: «ничего не ждём» или «ждём выбор из меню».</summary>
public sealed class ChatSession
{
    public PendingChoice? Pending { get; private set; }

    public void Wait(PendingChoice choice) => Pending = choice;

    public void Reset() => Pending = null;
}
