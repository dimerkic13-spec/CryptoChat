using System.Text;
using CryptoChat.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoChat.Cli;

/// <summary>
/// База бесед SQLite. Файл logs/cryptochat.db хранит:
///   sessions    — беседы: номер, время начала и конца, компьютер, версия, морфология, отправлена ли на почту;
///   messages    — все реплики пользователя и бота (тип ответа, id вопроса из базы, оценка);
///   feedback    — отзывы «да» / «нет» об ответах;
///   unanswered  — запросы, на которые бот не ответил сразу (уточнял тему, не понял или отправил в поддержку).
///
/// По этим данным команда --stats показывает, какие вопросы спрашивают чаще,
/// какие ответы не помогают и чего не хватает в базе знаний.
///
/// База бесед не заменяет базу знаний: вопросы и ответы остаются в content/*.json — их удобно
/// править и хранить в Git. Ошибки записи в базу не останавливают диалог.
/// Схема та же, что в Python-версии: базы обеих версий читаются одинаково.
/// </summary>
internal sealed class ChatDatabase : IDisposable
{
    private const int SchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS sessions (
            id          TEXT PRIMARY KEY,
            started     TEXT NOT NULL,
            finished    TEXT,
            computer    TEXT,
            version     TEXT,
            morphology  TEXT,
            messages    INTEGER NOT NULL DEFAULT 0,
            emailed     TEXT
        );
        CREATE TABLE IF NOT EXISTS messages (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id  TEXT NOT NULL REFERENCES sessions(id),
            time        TEXT NOT NULL,
            role        TEXT NOT NULL CHECK (role IN ('user', 'bot')),
            kind        TEXT,
            text        TEXT NOT NULL,
            question_id TEXT,
            score       REAL
        );
        CREATE TABLE IF NOT EXISTS feedback (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id  TEXT,
            time        TEXT NOT NULL,
            query       TEXT,
            question_id TEXT NOT NULL,
            helpful     INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS unanswered (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id  TEXT,
            time        TEXT NOT NULL,
            query       TEXT NOT NULL,
            kind        TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_messages_session ON messages(session_id);
        CREATE INDEX IF NOT EXISTS ix_messages_question ON messages(question_id);
        CREATE INDEX IF NOT EXISTS ix_feedback_question ON feedback(question_id);
        """;

    public static readonly IReadOnlySet<ReplyKind> UnansweredKinds =
        new HashSet<ReplyKind> { ReplyKind.Escalate, ReplyKind.NotUnderstood, ReplyKind.ClarifyBranch };

    private SqliteConnection? _connection;

    public string FilePath { get; }
    public bool Available { get; private set; } = true;
    public string? Error { get; private set; }

    public ChatDatabase(string path)
    {
        FilePath = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                DefaultTimeout = 5,
                Pooling = false // не оставлять файл базы занятым после закрытия
            }.ToString());
            _connection.Open();
            Execute("PRAGMA foreign_keys = ON");
            Execute(Schema);
            Execute($"PRAGMA user_version = {SchemaVersion}");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Available = false;
            Error = ex.Message;
            _connection?.Dispose();
            _connection = null;
        }
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    // ---------- запись ----------

    private void Run(string sql, params (string Name, object? Value)[] parameters)
    {
        if (!Available)
            return;
        try
        {
            Execute(sql, parameters);
        }
        catch (SqliteException ex)
        {
            Available = false; // не мешаем диалогу: дальше беседа просто не пишется в базу
            Error = ex.Message;
        }
    }

    public void StartSession(string sessionId, string computer, string version, string morphology) =>
        Run("INSERT OR IGNORE INTO sessions (id, started, computer, version, morphology) VALUES ($id, $started, $computer, $version, $morphology)",
            ("$id", sessionId), ("$started", Now()), ("$computer", computer), ("$version", version), ("$morphology", morphology));

    public void UserMessage(string sessionId, string text)
    {
        Run("INSERT INTO messages (session_id, time, role, text) VALUES ($session, $time, 'user', $text)",
            ("$session", sessionId), ("$time", Now()), ("$text", text));
        Run("UPDATE sessions SET messages = messages + 1 WHERE id = $id", ("$id", sessionId));
    }

    public void BotMessage(string sessionId, ChatReply reply) =>
        Run("INSERT INTO messages (session_id, time, role, kind, text, question_id, score) " +
            "VALUES ($session, $time, 'bot', $kind, $text, $question, $score)",
            ("$session", sessionId), ("$time", Now()), ("$kind", reply.Kind.ToString()), ("$text", reply.Text),
            ("$question", reply.Question?.Id), ("$score", reply.Question != null ? reply.Score : null));

    public void Unanswered(string? sessionId, string query, ReplyKind kind)
    {
        if (UnansweredKinds.Contains(kind))
            Run("INSERT INTO unanswered (session_id, time, query, kind) VALUES ($session, $time, $query, $kind)",
                ("$session", sessionId), ("$time", Now()), ("$query", query), ("$kind", kind.ToString()));
    }

    public void Feedback(string? sessionId, FeedbackEntry entry) =>
        Run("INSERT INTO feedback (session_id, time, query, question_id, helpful) VALUES ($session, $time, $query, $question, $helpful)",
            ("$session", sessionId), ("$time", entry.Time.ToString("yyyy-MM-dd HH:mm:ss")), ("$query", entry.Query),
            ("$question", entry.QuestionId), ("$helpful", entry.Helpful ? 1 : 0));

    public void FinishSession(string sessionId) =>
        Run("UPDATE sessions SET finished = $time WHERE id = $id", ("$time", Now()), ("$id", sessionId));

    public void MarkEmailed(string sessionId) =>
        Run("UPDATE sessions SET emailed = $time WHERE id = $id", ("$time", Now()), ("$id", sessionId));

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        Available = false;
    }

    // ---------- Статистика ----------

    /// <summary>Текстовый отчёт по базе бесед за всё время (подробнее — Analytics.Report).</summary>
    public static string StatsReport(string path, IReadOnlyDictionary<string, string>? titles = null, int limit = 10) =>
        Analytics.Report(path, titles == null ? Analytics.Catalog.Empty : Analytics.Catalog.FromTitles(titles),
            Analytics.Period.All, limit);
}
