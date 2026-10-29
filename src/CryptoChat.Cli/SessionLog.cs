using System.Security.Cryptography;
using System.Text;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Журнал беседы: у каждой беседы свой номер сессии и свой файл в папке logs/sessions/.
///
/// Реплики записываются в файл сразу, поэтому журнал не теряется, даже если окно закрыли крестиком
/// или компьютер выключился. В конце нормальной беседы в файл добавляется строка EndMarker —
/// по ней видно, что сессия завершена и её можно отправлять на почту.
/// </summary>
internal sealed class SessionRecorder
{
    public const string EndMarker = "=== Конец сессии";
    public const string SentSuffix = ".sent";

    private bool _closed;

    public string SessionId { get; }
    public DateTime Started { get; }
    public string FilePath { get; }

    /// <summary>Сколько сообщений написал пользователь.</summary>
    public int Messages { get; private set; }

    /// <summary>false, если файл журнала создать не удалось (например, нет прав на папку).</summary>
    public bool Available { get; private set; } = true;

    public SessionRecorder(string folder, string version, string morphology, string? sessionId = null,
        DateTime? now = null)
    {
        Started = now ?? DateTime.Now;
        SessionId = sessionId ?? NewSessionId(Started);
        FilePath = Path.Combine(folder, SessionId + ".txt");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Available = false;
        }
        Write($"CryptoChat {version} — журнал беседы\n" +
              $"Сессия: {SessionId}\n" +
              $"Начало: {Started:yyyy-MM-dd HH:mm:ss}\n" +
              $"Компьютер: {HostName()}\n" +
              $"Морфология: {morphology}\n" +
              $"{new string('-', 60)}\n");
    }

    /// <summary>Номер сессии: дата, время и 4 случайных символа, например 20261028-084012-7f3a.</summary>
    public static string NewSessionId(DateTime? now = null) =>
        $"{now ?? DateTime.Now:yyyyMMdd-HHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2))}";

    public static string HostName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return "неизвестно";
        }
    }

    public void User(string text)
    {
        Messages++;
        Write($"\n[{DateTime.Now:HH:mm:ss}] Пользователь: {text}\n");
    }

    public void Bot(ChatReply reply)
    {
        var lines = reply.Text.Split('\n');
        var body = lines[0] + string.Concat(lines.Skip(1).Select(line => "\n    " + line));
        Write($"[{DateTime.Now:HH:mm:ss}] Бот ({reply.Kind}): {body}\n");
    }

    /// <summary>Итог беседы (вопросы, ответы, отзывы) — пишется перед строкой конца сессии.</summary>
    public void Summary(IEnumerable<string> lines)
    {
        if (!_closed)
            Write("\n" + string.Join("\n", lines) + "\n");
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        Write($"\n{EndMarker}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, сообщений: {Messages}\n");
    }

    private void Write(string text)
    {
        if (!Available)
            return;
        try
        {
            File.AppendAllText(FilePath, text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Available = false; // журнал не должен ломать диалог
        }
    }

    // ---------- Поиск сессий для отправки ----------

    public static string SentMarker(string path) => path + SentSuffix;

    public static bool IsSent(string path) => File.Exists(SentMarker(path));

    public static void MarkSent(string path)
    {
        try
        {
            File.WriteAllText(SentMarker(path), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не удалось пометить: журнал просто уйдёт ещё раз.
        }
    }

    public static int CountMessages(string text)
    {
        const string marker = "] Пользователь: ";
        int count = 0;
        for (int i = text.IndexOf(marker, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(marker, i + marker.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// Неотправленные журналы, которые пора отправить.
    /// Берутся завершённые сессии (есть EndMarker) и «брошенные» — без маркера, но не менявшиеся
    /// дольше staleHours часов (окно закрыли крестиком или программа упала). Сессии, которые,
    /// возможно, ещё идут в другом окне, пропускаются.
    /// </summary>
    public static IReadOnlyList<string> PendingSessions(string folder, double staleHours = 12.0,
        string? skip = null, DateTime? now = null)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        var moment = now ?? DateTime.Now;
        var result = new List<string>();
        var files = Directory.GetFiles(folder, "*.txt")
            .Where(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) // в Windows «*.txt» находит и «*.txtx»
            .OrderBy(p => p, StringComparer.Ordinal);
        foreach (var path in files)
        {
            if (Path.GetFileNameWithoutExtension(path) == skip || IsSent(path))
                continue;
            string text;
            double ageHours;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
                ageHours = (moment - File.GetLastWriteTime(path)).TotalHours;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (text.Contains(EndMarker, StringComparison.Ordinal) || ageHours >= staleHours)
                result.Add(path);
        }
        return result;
    }
}
