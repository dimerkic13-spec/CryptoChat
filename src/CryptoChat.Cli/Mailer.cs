using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CryptoChat.Core.Models;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CryptoChat.Cli;

/// <summary>Письмо не отправлено: понятное объяснение причины.</summary>
internal sealed class MailException : Exception
{
    public MailException(string message) : base(message) { }
    public MailException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>То, что отправляет готовое письмо. В тестах подменяется.</summary>
internal interface IMailTransport
{
    void Send(EmailConfig config, string password, MimeMessage message);
}

/// <summary>
/// Отправка журнала беседы на почту (SMTP, библиотека MailKit).
///
/// Пароль ящика-отправителя НЕ хранится в config.json (он попадает в Git). Он берётся:
///   1) из переменной окружения, имя которой задано в email.passwordEnv (по умолчанию CRYPTOCHAT_SMTP_PASSWORD);
///   2) иначе из файла secrets.json в папке данных (--data-dir) или рядом с программой: { "smtpPassword": "..." }.
/// Файл secrets.json исключён из Git (.gitignore).
/// </summary>
internal static class Mailer
{
    public const string SecretsFile = "secrets.json";

    public static string LoadPassword(EmailConfig config, string appDir)
    {
        var fromEnv = string.IsNullOrEmpty(config.PasswordEnv) ? null : Environment.GetEnvironmentVariable(config.PasswordEnv);
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;

        var path = Path.Combine(appDir, SecretsFile);
        if (File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("smtpPassword", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } password)
                    return password;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new MailException($"Не удалось прочитать {path}: {ex.Message}", ex);
            }
        }

        throw new MailException(
            $"Не задан пароль почты. Укажите его в файле {path} как {{\"smtpPassword\": \"...\"}} " +
            $"или в переменной окружения {config.PasswordEnv}.");
    }

    public static MimeMessage BuildMessage(EmailConfig config, string subject, string body, string? attachment = null)
    {
        var message = new MimeMessage();
        try
        {
            message.From.Add(MailboxAddress.Parse(config.FromAddress()));
            foreach (var address in config.Recipients())
                message.To.Add(MailboxAddress.Parse(address));
        }
        catch (ParseException ex)
        {
            throw new MailException($"Неверный адрес почты в config.json (email.from / email.to): {ex.Message}", ex);
        }
        message.Subject = subject;

        var builder = new BodyBuilder { TextBody = body };
        if (attachment != null)
            builder.Attachments.Add(Path.GetFileName(attachment), File.ReadAllBytes(attachment),
                new ContentType("text", "plain") { Charset = "utf-8" });
        message.Body = builder.ToMessageBody();
        return message;
    }

    /// <summary>Отправляет письмо. transport подменяется в тестах.</summary>
    public static void Send(EmailConfig config, string password, MimeMessage message, IMailTransport? transport = null)
    {
        try
        {
            (transport ?? new SmtpTransport()).Send(config, password, message);
        }
        catch (AuthenticationException ex)
        {
            throw new MailException("Почтовый сервер не принял логин или пароль. Для Яндекс, Mail.ru и Gmail " +
                                    "нужен специальный «пароль приложения», а не обычный пароль от ящика.", ex);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException
                                       or SslHandshakeException or SocketException or IOException
                                       or TimeoutException or OperationCanceledException or FormatException)
        {
            throw new MailException($"Не удалось отправить письмо через {config.SmtpHost}:{config.SmtpPort}: {ex.Message}", ex);
        }
    }

    /// <summary>Отправляет один журнал. Возвращает false, если журнал пустой и пустые не отправляются.</summary>
    public static bool SendSession(EmailConfig config, string password, string path, IMailTransport? transport = null)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var messages = SessionRecorder.CountMessages(text);
        if (messages == 0 && !config.SendEmpty)
        {
            SessionRecorder.MarkSent(path); // пустую сессию больше не проверяем
            return false;
        }
        var subject = $"{config.SubjectPrefix}: беседа {Path.GetFileNameWithoutExtension(path)} (сообщений: {messages})";
        Send(config, password, BuildMessage(config, subject, text, path), transport);
        SessionRecorder.MarkSent(path);
        return true;
    }

    /// <summary>Отправляет все готовые неотправленные журналы. Возвращает число отправленных писем.</summary>
    public static int SendPending(EmailConfig config, string appDir, string folder, string? skip = null,
        Action<string>? log = null, IMailTransport? transport = null, Action<string>? onSent = null)
    {
        log ??= Console.WriteLine;
        var paths = SessionRecorder.PendingSessions(folder, skip: skip);
        if (paths.Count == 0)
            return 0;

        var password = LoadPassword(config, appDir);
        int sent = 0;
        foreach (var path in paths)
        {
            if (!SendSession(config, password, path, transport))
                continue;
            sent++;
            var id = Path.GetFileNameWithoutExtension(path);
            log($"Журнал {id} отправлен на {string.Join(", ", config.Recipients())}.");
            onSent?.Invoke(id);
        }
        return sent;
    }

    public static void SendTest(EmailConfig config, string appDir, IMailTransport? transport = null)
    {
        var password = LoadPassword(config, appDir);
        var message = BuildMessage(config, $"{config.SubjectPrefix}: проверка почты",
            "Это тестовое письмо CryptoChat. Если вы его получили, отправка журналов настроена.");
        Send(config, password, message, transport);
    }

    private sealed class SmtpTransport : IMailTransport
    {
        public void Send(EmailConfig config, string password, MimeMessage message)
        {
            var security = config.Security.ToLowerInvariant() switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "starttls" => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None
            };
            using var client = new SmtpClient { Timeout = config.TimeoutSeconds * 1000 };
            client.Connect(config.SmtpHost, config.SmtpPort, security);
            if (config.Username.Length > 0)
                client.Authenticate(config.Username, password);
            client.Send(message);
            client.Disconnect(true);
        }
    }
}
