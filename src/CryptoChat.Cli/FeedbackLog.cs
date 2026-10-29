using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Необязательный журнал отзывов об ответах (config.json → logging.feedback).
/// В каждой строке: время, номер сессии, вопрос пользователя, id вопроса из базы и оценка. Ошибки записи игнорируются.
/// </summary>
internal sealed class FeedbackLog
{
    private readonly string? _path;
    private readonly string? _session;

    public FeedbackLog(AppConfig config, string? sessionId = null)
    {
        _session = sessionId;
        if (config.Logging.Feedback)
            _path = ConsoleApp.LogPath(config.Logging.FeedbackPath);
    }

    public void Write(FeedbackEntry entry)
    {
        if (_path == null)
            return;
        UnansweredLog.AppendJson(_path, new
        {
            time = entry.Time.ToString("s"),
            session = _session,
            query = entry.Query,
            questionId = entry.QuestionId,
            helpful = entry.Helpful
        });
    }
}
