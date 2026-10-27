using System.Text.Encodings.Web;
using System.Text.Json;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Необязательный журнал вопросов, на которые бот не ответил (config.json → logging.unanswered).
/// По нему видно, какие вопросы стоит добавить в базу. Ошибки записи игнорируются.
/// </summary>
internal sealed class UnansweredLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string? _path;

    public UnansweredLog(AppConfig config)
    {
        if (!config.Logging.Unanswered)
            return;

        var path = config.Logging.Path;
        _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    public void Write(string query, ReplyKind kind)
    {
        if (_path == null || kind is not (ReplyKind.Escalate or ReplyKind.NotUnderstood))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(
                new { time = DateTime.Now.ToString("s"), query, kind = kind.ToString() }, JsonOptions);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch
        {
            // Журнал не должен ломать диалог.
        }
    }
}
