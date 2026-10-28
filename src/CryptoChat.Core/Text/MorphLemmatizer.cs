using System.Text.RegularExpressions;
using CryptoChat.Core.Text.Morphology;

namespace CryptoChat.Core.Text;

/// <summary>
/// Начальная форма слова по словарям pymorphy3 («рутокена» → «рутокен», «присоединились» → «присоединиться»).
/// Те же словари и тот же порядок разбора, что в Python-версии, поэтому основы слов совпадают.
///
/// Слова без кириллицы («ngate», «5.0», «r4») оставляются как есть.
/// Результаты запоминаются: одно слово разбирается только один раз.
/// </summary>
public sealed class MorphLemmatizer : ILemmatizer
{
    /// <summary>Папка со словарями рядом с программой.</summary>
    public const string DictionaryFolder = "dicts/pymorphy3-ru";

    /// <summary>Переменная окружения с другим путём к словарям.</summary>
    public const string PathVariable = "CRYPTOCHAT_DICTS";

    private static readonly Regex Cyrillic = new("[а-яё]", RegexOptions.Compiled);
    private static readonly object LoadLock = new();
    private static MorphLemmatizer? _shared;

    private readonly MorphAnalyzer _analyzer;
    private readonly Dictionary<string, string> _cache = new();
    private readonly object _cacheLock = new();

    public string Name => "pymorphy3";

    /// <summary>Откуда загружены словари.</summary>
    public string DictionaryPath { get; }

    private MorphLemmatizer(MorphAnalyzer analyzer, string path)
    {
        _analyzer = analyzer;
        DictionaryPath = path;
    }

    /// <summary>
    /// Загружает словари (один раз за запуск программы). Путь: параметр path, переменная окружения
    /// CRYPTOCHAT_DICTS, папка dicts/pymorphy3-ru рядом с программой или в текущей папке.
    /// </summary>
    public static MorphLemmatizer Load(string? path = null)
    {
        lock (LoadLock)
        {
            var folder = path ?? FindDictionaries();
            if (path == null && _shared != null && _shared.DictionaryPath == folder)
                return _shared;

            if (folder == null || !File.Exists(Path.Combine(folder, "words.dawg")))
                throw new MorphologyUnavailableException(
                    $"Не найдены словари морфологии: нужна папка {DictionaryFolder} рядом с программой " +
                    $"(или путь к ней в переменной окружения {PathVariable}).");
            try
            {
                var lemmatizer = new MorphLemmatizer(new MorphAnalyzer(MorphDictionary.Load(folder)), folder);
                if (path == null)
                    _shared = lemmatizer;
                return lemmatizer;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                           or System.Text.Json.JsonException or KeyNotFoundException
                                           or FormatException or EndOfStreamException)
            {
                throw new MorphologyUnavailableException($"Не удалось загрузить словари морфологии из {folder}: {ex.Message}", ex);
            }
        }
    }

    private static string? FindDictionaries()
    {
        var fromEnv = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var candidate = Path.Combine(root, DictionaryFolder);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public string Lemmatize(string word)
    {
        word = Normalizer.Normalize(word);
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(word, out var cached))
                return cached;
        }

        var result = word;
        if (Cyrillic.IsMatch(word))
        {
            var parses = _analyzer.Parse(word);
            if (parses.Count > 0)
            {
                var normal = Normalizer.Normalize(parses[0].NormalForm);
                result = normal.Length > 0 ? normal : word;
            }
        }

        lock (_cacheLock)
            _cache[word] = result;
        return result;
    }
}
