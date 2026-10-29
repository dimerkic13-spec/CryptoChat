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

    /// <summary>Текстовый отчёт по базе бесед для команды --stats.</summary>
    public static string StatsReport(string path, IReadOnlyDictionary<string, string>? titles = null, int limit = 10)
    {
        if (!File.Exists(path))
            return $"База бесед ещё не создана: {path}\nОна появится после первой беседы (config.json → database.enabled).";
        titles ??= new Dictionary<string, string>();

        string Title(string questionId)
        {
            var text = titles.GetValueOrDefault(questionId, "");
            return text.Length > 60 ? text[..60] + "…" : text;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString());
        connection.Open();

        List<object?[]> Rows(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            using var reader = command.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < row.Length; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }

        long Count(string sql) => Convert.ToInt64(Rows(sql)[0][0]);

        var summary = Rows("SELECT COUNT(*), MIN(started), MAX(started) FROM sessions")[0];
        var sessions = Convert.ToInt64(summary[0]);
        var users = Count("SELECT COUNT(*) FROM messages WHERE role = 'user'");
        var answers = Count("SELECT COUNT(*) FROM messages WHERE role = 'bot' AND kind = 'Answer'");
        var votes = Rows("SELECT COALESCE(SUM(helpful), 0), COALESCE(SUM(1 - helpful), 0) FROM feedback")[0];
        var unansweredTotal = Count("SELECT COUNT(*) FROM unanswered");
        var emailed = Count("SELECT COUNT(*) FROM sessions WHERE emailed IS NOT NULL");

        var lines = new List<string> { $"База бесед: {path}" };
        if (sessions == 0)
        {
            lines.Add("Бесед пока нет.");
            return string.Join("\n", lines);
        }
        var first = (string)summary[1]!;
        var last = (string)summary[2]!;
        lines.Add($"Бесед: {sessions} (с {first[..Math.Min(10, first.Length)]} по {last[..Math.Min(10, last.Length)]}), отправлено на почту: {emailed}");
        lines.Add($"Сообщений пользователей: {users}, выдано ответов: {answers}, не ответил сразу: {unansweredTotal}");
        lines.Add($"Отзывы: помогло {votes[0]}, не помогло {votes[1]}");

        var top = Rows("""
            SELECT m.question_id, COUNT(*) AS shown,
                   (SELECT COALESCE(SUM(helpful), 0) FROM feedback f WHERE f.question_id = m.question_id),
                   (SELECT COALESCE(SUM(1 - helpful), 0) FROM feedback f WHERE f.question_id = m.question_id)
            FROM messages m WHERE m.role = 'bot' AND m.kind = 'Answer' AND m.question_id IS NOT NULL
            GROUP BY m.question_id ORDER BY shown DESC, m.question_id LIMIT $limit
            """, ("$limit", limit));
        if (top.Count > 0)
        {
            lines.Add($"\nЧаще всего выдавались ответы (топ {limit}):");
            foreach (var row in top)
            {
                var id = (string)row[0]!;
                lines.Add($"  {id,-10} выдан: {row[1],-4} помогло: {row[2],-3} не помогло: {row[3],-3} {Title(id)}");
            }
        }

        var bad = Rows("""
            SELECT question_id, COUNT(*) FROM feedback WHERE helpful = 0
            GROUP BY question_id ORDER BY COUNT(*) DESC, question_id LIMIT $limit
            """, ("$limit", limit));
        if (bad.Count > 0)
        {
            lines.Add("\nОтветы с отзывом «не помогло» — стоит проверить текст и ключевые слова:");
            foreach (var row in bad)
            {
                var id = (string)row[0]!;
                var queries = Rows("SELECT query FROM feedback WHERE helpful = 0 AND question_id = $id " +
                                   "ORDER BY time DESC LIMIT 3", ("$id", id));
                var examples = string.Join("; ", queries
                    .Select(q => q[0] as string)
                    .Where(q => !string.IsNullOrEmpty(q))
                    .Select(q => $"«{q}»"));
                lines.Add($"  {id,-10} не помогло: {row[1],-4} {Title(id)}");
                if (examples.Length > 0)
                    lines.Add($"             запросы: {examples}");
            }
        }

        // Запросы складываются без учёта регистра. Делается в программе: LOWER() в SQLite
        // переводит в нижний регистр только латиницу.
        var grouped = new Dictionary<string, (int Count, string Last)>();
        foreach (var row in Rows("SELECT query, time FROM unanswered"))
        {
            var query = (string)row[0]!;
            var time = (string)row[1]!;
            var key = string.Join(" ", query.ToLowerInvariant().Replace('ё', 'е')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var item = grouped.GetValueOrDefault(key, (Count: 0, Last: ""));
            grouped[key] = (item.Count + 1, string.CompareOrdinal(time, item.Last) > 0 ? time : item.Last);
        }
        // Сначала частые, при равенстве — более свежие.
        var missing = grouped
            .OrderByDescending(x => x.Value.Count)
            .ThenByDescending(x => x.Value.Last, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        if (missing.Count > 0)
        {
            lines.Add("\nЗапросы, на которые бот не ответил сразу — кандидаты в новые вопросы или ключи:");
            foreach (var (query, (count, lastTime)) in missing)
                lines.Add($"  повторов: {count,-4} «{query}»  (последний: {lastTime[..Math.Min(16, lastTime.Length)]})");
        }

        return string.Join("\n", lines);
    }
}
