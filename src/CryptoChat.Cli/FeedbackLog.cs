using System.Text.Encodings.Web;
using System.Text.Json;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Необязательный журнал отзывов об ответах (config.json → logging.feedback).
/// В каждой строке: время, вопрос пользователя, id вопроса из базы и оценка. Ошибки записи игнорируются.
/// </summary>
internal sealed class FeedbackLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string? _path;

    public FeedbackLog(AppConfig config)
    {
        if (!config.Logging.Feedback)
            return;

        var path = config.Logging.FeedbackPath;
        _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    public void Write(FeedbackEntry entry)
    {
        if (_path == null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(
                new
                {
                    time = entry.Time.ToString("s"),
                    query = entry.Query,
                    questionId = entry.QuestionId,
                    helpful = entry.Helpful
                },
                JsonOptions);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch
        {
            // Журнал не должен ломать диалог.
        }
    }
}
