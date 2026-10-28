using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

public interface IRetriever
{
    /// <summary>
    /// Ищет вопросы по леммам. forcedScope задаёт область принудительно (выбор продукта или ветки пользователем);
    /// query — исходный текст запроса (нужен для поиска по смыслу).
    /// </summary>
    RetrievalResult Search(IReadOnlyList<string> lemmas, Category? forcedScope = null, string? query = null);
}
