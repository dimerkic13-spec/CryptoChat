namespace CryptoChat.Core.Knowledge;

/// <summary>Ошибка загрузки или проверки данных. Сообщение написано для человека.</summary>
public sealed class ContentException : Exception
{
    public ContentException(string message) : base(message) { }
    public ContentException(string message, Exception inner) : base(message, inner) { }
}
