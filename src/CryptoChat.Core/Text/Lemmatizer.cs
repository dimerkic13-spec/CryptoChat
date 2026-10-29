namespace CryptoChat.Core.Text;

/// <summary>
/// Приводит уже нормализованное слово к основе. Один и тот же лемматизатор применяется
/// к запросу, ключевым словам и синонимам, поэтому они всегда сравниваются в одном виде.
/// </summary>
public interface ILemmatizer
{
    /// <summary>Как называть способ в выводе программы: «стеммер», «pymorphy3».</summary>
    string Name { get; }

    string Lemmatize(string word);
}

/// <summary>Простой стеммер: отрезает окончания по 5 правилам («лицензию» → «лицензи»).</summary>
public sealed class StemmerLemmatizer : ILemmatizer
{
    public static StemmerLemmatizer Instance { get; } = new();

    public string Name => "стеммер";

    public string Lemmatize(string word) => Stemmer.Stem(word);
}

/// <summary>Морфологию нельзя включить: нет словарей или они повреждены.</summary>
public sealed class MorphologyUnavailableException : Exception
{
    public MorphologyUnavailableException(string message) : base(message) { }
    public MorphologyUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Создаёт лемматизатор по режиму из config.json → text.morphology.</summary>
public static class Lemmatizers
{
    public static readonly string[] Modes = { "stemmer", "pymorphy3", "auto" };

    /// <summary>
    /// Возвращает лемматизатор и примечание. Примечание не пустое, если режим «auto»
    /// пришлось заменить стеммером, потому что словари морфологии не найдены.
    /// </summary>
    /// <param name="morphologyFactory">Как создать морфологию (в тестах подменяется).</param>
    public static (ILemmatizer Lemmatizer, string? Note) Create(
        string? mode, Func<ILemmatizer>? morphologyFactory = null)
    {
        var factory = morphologyFactory ?? (() => MorphLemmatizer.Load());
        switch ((mode ?? "stemmer").ToLowerInvariant())
        {
            case "stemmer":
                return (StemmerLemmatizer.Instance, null);
            case "pymorphy3":
                return (factory(), null); // MorphologyUnavailableException, если словарей нет
            case "auto":
                try
                {
                    return (factory(), null);
                }
                catch (MorphologyUnavailableException)
                {
                    return (StemmerLemmatizer.Instance, "словари морфологии не найдены, используется стеммер");
                }
            default:
                throw new ArgumentException(
                    $"Неизвестный режим морфологии «{mode}». Допустимо: {string.Join(", ", Modes)}.");
        }
    }
}
