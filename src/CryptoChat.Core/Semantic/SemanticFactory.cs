using CryptoChat.Core.Models;

namespace CryptoChat.Core.Semantic;

public static class SemanticFactory
{
    /// <summary>
    /// Создаёт индекс по настройкам config.json → semantic.
    /// Возвращает (индекс или null, примечание для пользователя). Если поиск по смыслу выключен,
    /// примечание пустое; если включён, но недоступен, — объяснение, почему.
    /// </summary>
    /// <param name="appDir">Папка программы: от неё считается semantic.cacheDir.</param>
    /// <param name="encoderFactory">Как создать модель по папке (в тестах подменяется).</param>
    public static (SemanticIndex? Index, string? Note) Create(SemanticConfig config, IEnumerable<Question> questions,
        string? appDir = null, Func<string, IEncoder>? encoderFactory = null)
    {
        if (!config.Enabled)
            return (null, null);

        var folder = OnnxEncoder.ModelFolder(CacheDir(config, appDir), config.Model);
        IEncoder encoder;
        try
        {
            encoder = (encoderFactory ?? (f => OnnxEncoder.Load(f)))(folder);
        }
        catch (SemanticUnavailableException ex)
        {
            return (null, ex.Message);
        }

        try
        {
            var index = new SemanticIndex(encoder, questions, config.Low, config.High, config.Weight, encoder.Name,
                config.Mode, config.ZLow, config.ZHigh, config.TopK, config.QueryPrefix, config.PassagePrefix);
            return (index, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, $"Не удалось посчитать векторы вопросов: {ex.Message}");
        }
    }

    /// <summary>Папка с моделями: semantic.cacheDir относительно папки программы.</summary>
    public static string CacheDir(SemanticConfig config, string? appDir) =>
        Path.IsPathRooted(config.CacheDir) || appDir == null ? config.CacheDir : Path.Combine(appDir, config.CacheDir);

    /// <summary>Есть ли выгруженная модель (чтобы не писать «Загружаю модель…», когда загружать нечего).</summary>
    public static bool ModelExists(SemanticConfig config, string? appDir) =>
        File.Exists(Path.Combine(OnnxEncoder.ModelFolder(CacheDir(config, appDir), config.Model), "model.onnx"));
}
