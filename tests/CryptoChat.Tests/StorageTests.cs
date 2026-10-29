using System.Net;
using System.Net.Sockets;
using System.Text;
using CryptoChat.Cli;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using Microsoft.Data.Sqlite;
using MimeKit;

namespace CryptoChat.Tests;

/// <summary>Временная папка, которая удаляется после теста.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cryptochat-tests-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public class DatabaseTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private string DbPath => _tmp.Sub("logs", "cryptochat.db");

    public void Dispose() => _tmp.Dispose();

    private List<object?[]> Query(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
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

    private static Question FirstQuestion() => KnowledgeBase.Build(TestContent.Valid()).Questions[0];

    [Fact]
    public void Writes_SessionMessagesFeedbackUnanswered()
    {
        using (var db = new ChatDatabase(DbPath))
        {
            Assert.True(db.Available);
            db.StartSession("s1", "pc", "0.2.5a", "стеммер");
            db.UserMessage("s1", "как продлить лицензию");
            db.BotMessage("s1", new ChatReply(ReplyKind.Answer, "Ответ", Array.Empty<ChatOption>(), null, FirstQuestion(), 0.7));
            db.Unanswered("s1", "абракадабра", ReplyKind.ClarifyBranch);
            db.Unanswered("s1", "как купить лицензию", ReplyKind.Answer); // ответ — не пишется
            db.Feedback("s1", new FeedbackEntry(DateTime.Now, "как продлить лицензию", "1.1.1", false));
            db.FinishSession("s1");
            db.MarkEmailed("s1");
        }

        var session = Query("SELECT id, messages FROM sessions").Single();
        Assert.Equal(("s1", 1L), ((string)session[0]!, (long)session[1]!));
        var messages = Query("SELECT role, question_id, score FROM messages ORDER BY id");
        Assert.Equal(new object?[] { "user", null, null }, messages[0]);
        Assert.Equal(new object?[] { "bot", "1.1.1", 0.7 }, messages[1]);
        Assert.Equal(new object?[] { "абракадабра", "ClarifyBranch" }, Query("SELECT query, kind FROM unanswered").Single());
        Assert.Equal(new object?[] { "1.1.1", 0L }, Query("SELECT question_id, helpful FROM feedback").Single());
        Assert.NotNull(Query("SELECT finished, emailed FROM sessions")[0][1]);
    }

    [Fact]
    public void StatsReport()
    {
        using (var db = new ChatDatabase(DbPath))
        {
            db.StartSession("s1", "pc", "0.2.5a", "стеммер");
            db.BotMessage("s1", new ChatReply(ReplyKind.Answer, "Ответ", Array.Empty<ChatOption>(), null, FirstQuestion(), 0.7));
            db.Feedback("s1", new FeedbackEntry(DateTime.Now, "купить", "1.1.1", false));
            db.Unanswered("s1", "Абракадабра", ReplyKind.Escalate);
            db.Unanswered("s1", "абракадабра ", ReplyKind.ClarifyBranch);
        }

        var report = ChatDatabase.StatsReport(DbPath, new Dictionary<string, string> { ["1.1.1"] = "Как купить лицензию?" });
        Assert.Contains("Бесед: 1", report);
        Assert.Contains("Как купить лицензию?", report);
        Assert.Contains("не помогло: 1", report);
        Assert.Contains("повторов: 2", report); // одинаковые запросы с разным регистром сложены
        Assert.Contains("«купить»", report);
    }

    [Fact]
    public void StatsWithoutDatabase() => Assert.Contains("ещё не создана", ChatDatabase.StatsReport(DbPath));

    [Fact]
    public void UnwritablePath_DoesNotThrow()
    {
        var blocker = _tmp.Sub("file");
        File.WriteAllText(blocker, "x");
        using var db = new ChatDatabase(System.IO.Path.Combine(blocker, "sub", "cryptochat.db"));
        Assert.False(db.Available);
        db.UserMessage("s1", "ничего не сломается"); // просто ничего не делает
    }
}

public class SessionTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private string Folder => _tmp.Sub("sessions");

    public void Dispose() => _tmp.Dispose();

    private SessionRecorder Recorder() => new(Folder, "0.2.5a", "стеммер");

    [Fact]
    public void SessionId_Format()
    {
        Assert.Matches(@"^20261028-084012-[0-9a-f]{4}$", SessionRecorder.NewSessionId(new DateTime(2026, 10, 28, 8, 40, 12)));
    }

    [Fact]
    public void Ids_AreUnique()
    {
        Assert.Equal(50, Enumerable.Range(0, 50).Select(_ => SessionRecorder.NewSessionId()).Distinct().Count());
    }

    [Fact]
    public void Transcript_IsWrittenImmediately_AndClosedOnce()
    {
        var rec = Recorder();
        rec.User("Забыл пароль");
        rec.Bot(new ChatReply(ReplyKind.Clarify, "Уточните:\n  1. Вариант", Array.Empty<ChatOption>()));
        var text = File.ReadAllText(rec.FilePath);
        Assert.Contains(rec.SessionId, text);
        Assert.Contains("Пользователь: Забыл пароль", text);
        Assert.Contains("Бот (Clarify): Уточните:\n      1. Вариант", text);
        Assert.DoesNotContain(SessionRecorder.EndMarker, text);

        rec.Close();
        rec.Close();
        text = File.ReadAllText(rec.FilePath);
        Assert.Equal(1, text.Split(SessionRecorder.EndMarker).Length - 1);
        Assert.Contains("сообщений: 1", text);
    }

    [Fact]
    public void Pending_TakesFinishedAndStale_ButNotRunning()
    {
        var done = Recorder(); done.User("вопрос"); done.Close();
        var running = Recorder(); running.User("ещё идёт");
        var abandoned = Recorder(); abandoned.User("окно закрыли крестиком");
        File.SetLastWriteTime(abandoned.FilePath, DateTime.Now.AddHours(-13));

        var names = SessionRecorder.PendingSessions(Folder).Select(System.IO.Path.GetFileNameWithoutExtension).ToList();
        Assert.Contains(done.SessionId, names);
        Assert.Contains(abandoned.SessionId, names);
        Assert.DoesNotContain(running.SessionId, names);
        Assert.DoesNotContain(done.SessionId,
            SessionRecorder.PendingSessions(Folder, skip: done.SessionId).Select(System.IO.Path.GetFileNameWithoutExtension));
    }
}

/// <summary>Подмена почтового сервера: запоминает отправленные письма.</summary>
internal sealed class FakeTransport : IMailTransport
{
    public List<MimeMessage> Sent { get; } = new();
    public (string User, string Password)? Login { get; private set; }

    public void Send(EmailConfig config, string password, MimeMessage message)
    {
        if (config.Username.Length > 0)
            Login = (config.Username, password);
        Sent.Add(message);
    }
}

public class MailerTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private string Folder => _tmp.Sub("sessions");

    public void Dispose() => _tmp.Dispose();

    internal static EmailConfig Config(string security = "none", int port = 25, string username = "") => new()
    {
        Enabled = true, SmtpHost = "127.0.0.1", SmtpPort = port, Security = security,
        Username = username, Sender = "bot@example.ru", To = new List<string> { "boss@example.ru" }, TimeoutSeconds = 5
    };

    private SessionRecorder Recorder() => new(Folder, "0.2.5a", "стеммер");

    [Fact]
    public void SendSession_MarksSent()
    {
        var rec = Recorder(); rec.User("как купить лицензию"); rec.Close();
        var transport = new FakeTransport();

        Assert.True(Mailer.SendSession(Config(), "", rec.FilePath, transport));
        Assert.True(SessionRecorder.IsSent(rec.FilePath));
        var message = transport.Sent.Single();
        Assert.Contains(rec.SessionId, message.Subject);
        Assert.Equal("boss@example.ru", message.To.Mailboxes.Single().Address);
        Assert.Equal("bot@example.ru", message.From.Mailboxes.Single().Address);
        Assert.Equal($"{rec.SessionId}.txt", message.Attachments.OfType<MimePart>().Single().FileName);
        Assert.Empty(SessionRecorder.PendingSessions(Folder));
    }

    [Fact]
    public void EmptySession_IsNotSent()
    {
        var rec = Recorder(); rec.Close();
        var transport = new FakeTransport();
        Assert.False(Mailer.SendSession(Config(), "", rec.FilePath, transport));
        Assert.Empty(transport.Sent);
        Assert.True(SessionRecorder.IsSent(rec.FilePath)); // больше не проверяется
    }

    [Fact]
    public void SendPending_SendsAllReady()
    {
        for (int i = 0; i < 3; i++)
        {
            var rec = Recorder(); rec.User($"вопрос {i}"); rec.Close();
        }
        File.WriteAllText(_tmp.Sub(Mailer.SecretsFile), "{\"smtpPassword\": \"pw\"}");
        var transport = new FakeTransport();
        var config = Config(username: "bot@example.ru");
        config.PasswordEnv = "NO_SUCH_VAR_CRYPTOCHAT";

        Assert.Equal(3, Mailer.SendPending(config, _tmp.Path, Folder, log: _ => { }, transport: transport));
        Assert.Equal(3, transport.Sent.Count);
        Assert.Equal(("bot@example.ru", "pw"), transport.Login);
    }

    [Fact]
    public void Password_FromEnvironmentVariable_Wins()
    {
        Environment.SetEnvironmentVariable("CRYPTOCHAT_TEST_PW", "from-env");
        try
        {
            var config = Config();
            config.PasswordEnv = "CRYPTOCHAT_TEST_PW";
            File.WriteAllText(_tmp.Sub(Mailer.SecretsFile), "{\"smtpPassword\": \"from-file\"}");
            Assert.Equal("from-env", Mailer.LoadPassword(config, _tmp.Path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRYPTOCHAT_TEST_PW", null);
        }
    }

    [Fact]
    public void Password_FromSecretsFile()
    {
        var config = Config();
        config.PasswordEnv = "NO_SUCH_VAR_CRYPTOCHAT";
        File.WriteAllText(_tmp.Sub(Mailer.SecretsFile), "{\"smtpPassword\": \"from-file\"}");
        Assert.Equal("from-file", Mailer.LoadPassword(config, _tmp.Path));
    }

    [Fact]
    public void MissingPassword_Explains()
    {
        var config = Config();
        config.PasswordEnv = "NO_SUCH_VAR_CRYPTOCHAT";
        var ex = Assert.Throws<MailException>(() => Mailer.LoadPassword(config, _tmp.Path));
        Assert.Contains("secrets.json", ex.Message);
    }

    [Fact]
    public void RealSmtp_DeliversToLocalServer()
    {
        using var server = new MiniSmtpServer();
        var config = Config(port: server.Port);
        Mailer.Send(config, "", Mailer.BuildMessage(config, "CryptoChat: проверка", "Тело письма"));
        var data = server.WaitForData();
        Assert.Contains("Subject:", data);
        Assert.Contains("boss@example.ru", data);
    }

    [Fact]
    public void UnreachableServer_GivesMailException()
    {
        var config = Config(port: 1);
        Assert.Throws<MailException>(() => Mailer.Send(config, "", Mailer.BuildMessage(config, "s", "b")));
    }

    /// <summary>Крошечный SMTP-сервер на localhost: проверяет, что настоящий клиент MailKit доставляет письмо.</summary>
    private sealed class MiniSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<string> _task;

        public int Port { get; }

        public MiniSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = Task.Run(Serve);
        }

        public string WaitForData() => _task.Wait(TimeSpan.FromSeconds(10)) ? _task.Result : "";

        private async Task<string> Serve()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            var data = new StringBuilder();
            await writer.WriteLineAsync("220 test");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 OK");
                    }
                    else
                    {
                        data.AppendLine(line);
                    }
                    continue;
                }
                var command = line.Split(' ', 2)[0].ToUpperInvariant();
                switch (command)
                {
                    case "EHLO":
                    case "HELO":
                        await writer.WriteLineAsync("250-test");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "DATA":
                        inData = true;
                        await writer.WriteLineAsync("354 go");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return data.ToString();
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
            return data.ToString();
        }

        public void Dispose() => _listener.Stop();
    }
}

public class EmailConfigTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void FromAlias_AndStringRecipient()
    {
        File.WriteAllText(_tmp.Sub("config.json"),
            "{\"email\": {\"enabled\": true, \"smtpHost\": \"smtp.yandex.ru\", \"from\": \"bot@ya.ru\", \"to\": \"a@x.ru, b@x.ru\"}}");
        var config = AppConfig.Load(_tmp.Path);
        Assert.Equal("bot@ya.ru", config.Email.FromAddress());
        Assert.Equal(new[] { "a@x.ru", "b@x.ru" }, config.Email.Recipients());
    }

    [Fact]
    public void EnabledWithoutRecipient_IsError()
    {
        File.WriteAllText(_tmp.Sub("config.json"),
            "{\"email\": {\"enabled\": true, \"smtpHost\": \"smtp.yandex.ru\", \"username\": \"bot@ya.ru\"}}");
        Assert.Throws<ContentException>(() => AppConfig.Load(_tmp.Path));
    }

    [Theory]
    [InlineData("{\"text\": {\"morphology\": \"magic\"}}")]
    [InlineData("{\"fuzzy\": {\"threshold\": 30}}")]
    [InlineData("{\"semantic\": {\"mode\": \"other\"}}")]
    [InlineData("{\"semantic\": {\"low\": 0.9, \"high\": 0.5}}")]
    [InlineData("{\"email\": {\"security\": \"tls13\"}}")]
    [InlineData("{\"dialog\": {\"suggestScore\": 2}}")]
    public void BadValues_Throw(string json)
    {
        File.WriteAllText(_tmp.Sub("config.json"), json);
        Assert.Throws<ContentException>(() => AppConfig.Load(_tmp.Path));
    }
}

public class ConversationTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Conversation_WritesFileAndDatabase()
    {
        var engine = ChatEngine.Create(TestSupport.FindContentDir(), "stemmer", semantic: false);
        engine.Config.Sessions.Path = _tmp.Sub("sessions");
        engine.Config.Database.Path = _tmp.Sub("cryptochat.db");
        engine.Config.Email.Enabled = false;

        var conversation = new Conversation(engine);
        foreach (var line in new[] { "Можно оплатить счёт на ИП банковской картой?", "да", "абракадабра", "выход" })
        {
            conversation.User(line);
            conversation.Bot(line, engine.Ask(line));
        }
        conversation.Close();

        Assert.True(File.Exists(_tmp.Sub("sessions", conversation.SessionId + ".txt")));
        using var connection = new SqliteConnection($"Data Source={_tmp.Sub("cryptochat.db")};Pooling=False");
        connection.Open();
        long Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
        Assert.Equal(3, Scalar("SELECT messages FROM sessions"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM feedback WHERE helpful = 1"));
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT query FROM unanswered";
        Assert.Equal("абракадабра", query.ExecuteScalar());
    }
}
