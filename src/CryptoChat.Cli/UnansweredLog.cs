using System.Text.Encodings.Web;
using System.Text.Json;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Необязательный журнал вопросов, на которые бот не смог сразу ответить: он уточнял тему или направил в поддержку (config.json → logging.unanswered).
/// По нему видно, какие вопросы стоит добавить в базу. Ошибки записи игнорируются.
/// </summary>
internal sealed class UnansweredLog
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string? _path;
    private readonly string? _session;

    public UnansweredLog(AppConfig config, string? sessionId = null)
    {
        _session = sessionId;
        if (config.Logging.Unanswered)
            _path = ConsoleApp.LogPath(config.Logging.Path);
    }

    public void Write(string query, ReplyKind kind)
    {
        if (_path == null || !ChatDatabase.UnansweredKinds.Contains(kind))
            return;
        AppendJson(_path, new { time = DateTime.Now.ToString("s"), session = _session, query, kind = kind.ToString() });
    }

    internal static void AppendJson(string path, object record)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Журнал не должен ломать диалог.
        }
    }
}
