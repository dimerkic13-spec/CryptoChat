using System.Text;
using System.Text.Json;

namespace CryptoChat.Core.Knowledge;

/// <summary>Читает каталог content/, проверяет данные и строит базу знаний.</summary>
public static class KnowledgeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Читает файлы как есть, без проверки содержимого.</summary>
    public static ContentSet LoadContent(string directory)
    {
        if (!Directory.Exists(directory))
            throw new ContentException($"Каталог с данными не найден: {directory}");

        var taxonomy = Read<RawTaxonomy>(Path.Combine(directory, "taxonomy.json"), required: true)!;
        var synonyms = Read<RawSynonyms>(Path.Combine(directory, "synonyms.json"), required: false)
                       ?? new RawSynonyms();

        var questions = new List<RawQuestion>();
        var questionsDir = Path.Combine(directory, "questions");
        if (Directory.Exists(questionsDir))
        {
            var files = Directory.GetFiles(questionsDir, "*.json")
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var file in files)
                questions.AddRange(Read<List<RawQuestion>>(file, required: true)!);
        }

        return new ContentSet { Taxonomy = taxonomy, Synonyms = synonyms, Questions = questions };
    }

    /// <summary>Читает, проверяет и строит базу. При ошибках в данных бросает ContentException.</summary>
    public static KnowledgeBase Load(string directory)
    {
        var content = LoadContent(directory);
        var errors = ContentValidator.Validate(content)
            .Where(i => i.Severity == Severity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            var text = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            throw new ContentException($"В данных найдены ошибки ({errors.Count}):{Environment.NewLine}{text}");
        }

        return KnowledgeBase.Build(content);
    }

    private static T? Read<T>(string path, bool required) where T : class
    {
        if (!File.Exists(path))
        {
            if (required)
                throw new ContentException($"Файл не найден: {path}");
            return null;
        }

        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(text, JsonOptions)
                   ?? throw new ContentException($"Файл пуст: {path}");
        }
        catch (JsonException ex)
        {
            throw new ContentException($"Ошибка в файле {path}: {ex.Message}", ex);
        }
    }
}
