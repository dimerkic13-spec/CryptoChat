using CryptoChat.Core;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>Всё, что сохраняется о беседе: файл журнала, база SQLite, журналы отзывов и неотвеченных.</summary>
internal sealed class Conversation
{
    private readonly AppConfig _config;
    private readonly UnansweredLog _log;
    private readonly SessionRecorder? _recorder;
    private readonly ChatDatabase? _db;
    private bool _closed;

    // Для итога беседы в журнале.
    private int _questions, _unanswered, _helpful, _unhelpful;
    private readonly List<(string Id, string Title, bool ViaMenu)> _answers = new();

    /// <summary>Итог беседы: те же строки, что пишутся в конец журнала.</summary>
    public IReadOnlyList<string> SummaryLines => Analytics.SummaryLines(_questions, _answers.Count,
        _answers.Count(a => a.ViaMenu), _unanswered, _helpful, _unhelpful, _answers.Select(a => (a.Id, a.Title)));

    public string? SessionId { get; }
    public ChatDatabase? Database => _db;

    public Conversation(ChatEngine engine)
    {
        _config = engine.Config;

        if (_config.Sessions.Enabled || _config.Database.Enabled)
            SessionId = SessionRecorder.NewSessionId();

        if (_config.Sessions.Enabled)
        {
            var recorder = new SessionRecorder(ConsoleApp.SessionsFolder(_config), ConsoleApp.Version(),
                engine.Morphology, SessionId);
            if (recorder.Available)
                _recorder = recorder;
            else
                Console.WriteLine($"Не удалось создать журнал беседы в {ConsoleApp.SessionsFolder(_config)}; файл беседы не пишется.");
        }

        if (_config.Database.Enabled)
        {
            var db = new ChatDatabase(ConsoleApp.DatabasePath(_config));
            if (db.Available)
            {
                _db = db;
                db.StartSession(SessionId!, SessionRecorder.HostName(), ConsoleApp.Version(), engine.Morphology);
            }
            else
            {
                Console.WriteLine($"Не удалось открыть базу бесед {ConsoleApp.DatabasePath(_config)}: {db.Error}");
            }
        }

        engine.SessionId = SessionId;
        _log = new UnansweredLog(_config, SessionId);
        var feedbackLog = new FeedbackLog(_config, SessionId);
        engine.FeedbackGiven += feedbackLog.Write;
        if (_db != null)
            engine.FeedbackGiven += entry => _db.Feedback(SessionId, entry);
        engine.FeedbackGiven += entry =>
        {
            if (entry.Helpful) _helpful++;
            else _unhelpful++;
        };
    }

    public void User(string line)
    {
        if (ChatEngine.ExitWords.Contains(line.Trim().ToLowerInvariant()))
            return;
        _recorder?.User(line);
        _db?.UserMessage(SessionId!, line);
    }

    public void Bot(string line, ChatReply reply)
    {
        _recorder?.Bot(reply);
        _db?.BotMessage(SessionId!, reply);
        var text = line.Trim();
        if (reply.Kind == ReplyKind.Answer && reply.Question != null)
            _answers.Add((reply.Question.Id, reply.Question.Title, IsDigits(text)));
        if (!IsDigits(text) && !text.StartsWith('/') && reply.Kind is not (ReplyKind.Feedback or ReplyKind.Command or ReplyKind.Exit))
            _questions++;
        if (!IsDigits(text) && ChatDatabase.UnansweredKinds.Contains(reply.Kind))
            _unanswered++;
        if (!IsDigits(text)) // номер пункта меню — не вопрос
        {
            _log.Write(line, reply.Kind);
            _db?.Unanswered(SessionId, line, reply.Kind);
        }
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        _recorder?.Summary(SummaryLines);
        _recorder?.Close();
        if (_db != null)
            _db.FinishSession(SessionId!);
        if (_recorder != null && _config.Email.Enabled)
        {
            Console.WriteLine($"Отправляю журнал беседы на {string.Join(", ", _config.Email.Recipients())}...");
            ConsoleApp.SendLogs(_config, db: _db);
        }
        _db?.Dispose();
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsDigit);
}
