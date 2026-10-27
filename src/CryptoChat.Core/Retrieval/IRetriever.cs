using CryptoChat.Core.Models;

namespace CryptoChat.Core.Retrieval;

public interface IRetriever
{
    /// <summary>Ищет вопросы по леммам. forcedScope задаёт область принудительно (выбор продукта пользователем).</summary>
    RetrievalResult Search(IReadOnlyList<string> lemmas, Category? forcedScope = null);
}
