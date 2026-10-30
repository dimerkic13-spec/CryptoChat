using CryptoChat.Cli;

namespace CryptoChat.Tests;

/// <summary>
/// 0.3.0a: журналы, база бесед и secrets.json — в папке данных пользователя (%LocalAppData%\CryptoChat),
/// а не рядом с программой: туда можно писать и после установки, и они не пропадают при пересборке.
/// </summary>
[Collection(nameof(DataDirectoryTests))] // меняет переменную окружения — не параллельно с собой
public class DataDirectoryTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private readonly string? _saved = Environment.GetEnvironmentVariable(ConsoleApp.DataEnvironmentVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConsoleApp.DataEnvironmentVariable, _saved);
        _tmp.Dispose();
    }

    [Fact]
    public void ByDefault_DataIsInLocalAppData()
    {
        Environment.SetEnvironmentVariable(ConsoleApp.DataEnvironmentVariable, null);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(local, "CryptoChat"), ConsoleApp.DataDirectory());
        Assert.Equal(Path.Combine(local, "CryptoChat", "logs", "cryptochat.db"), ConsoleApp.LogPath("logs/cryptochat.db"));
    }

    [Fact]
    public void EnvironmentVariable_OverridesDataFolder_AndAbsolutePathsStayAsIs()
    {
        Environment.SetEnvironmentVariable(ConsoleApp.DataEnvironmentVariable, _tmp.Path);
        Assert.Equal(_tmp.Path, ConsoleApp.DataDirectory());
        Assert.Equal(Path.Combine(_tmp.Path, "logs", "sessions"), ConsoleApp.LogPath(Path.Combine("logs", "sessions")));
        var absolute = _tmp.Sub("elsewhere", "cryptochat.db");
        Assert.Equal(absolute, ConsoleApp.LogPath(absolute));
    }

    [Fact]
    public void SecretsFile_IsLookedUpInDataFolder()
    {
        Environment.SetEnvironmentVariable(ConsoleApp.DataEnvironmentVariable, _tmp.Path);
        Assert.Equal(_tmp.Path, ConsoleApp.SecretsDirectory()); // нигде нет — подсказываем папку данных
        File.WriteAllText(_tmp.Sub(Mailer.SecretsFile), "{\"smtpPassword\": \"x\"}");
        Assert.Equal(_tmp.Path, ConsoleApp.SecretsDirectory());
    }

    [Fact]
    public void OldLogs_AreCopiedOnce()
    {
        var app = _tmp.Sub("app");
        var data = _tmp.Sub("data");
        Directory.CreateDirectory(Path.Combine(app, "logs", "sessions"));
        File.WriteAllText(Path.Combine(app, "logs", "cryptochat.db"), "база");
        File.WriteAllText(Path.Combine(app, "logs", "sessions", "s1.txt"), "беседа");

        var message = ConsoleApp.MigrateOldLogs(app, data);

        Assert.Contains("скопированы", message);
        Assert.Equal("база", File.ReadAllText(Path.Combine(data, "logs", "cryptochat.db")));
        Assert.Equal("беседа", File.ReadAllText(Path.Combine(data, "logs", "sessions", "s1.txt")));
        Assert.True(File.Exists(Path.Combine(app, "logs", "cryptochat.db"))); // старые не удаляются

        // Второй раз ничего не делаем: в папке данных журналы уже есть, новые беседы не затираются.
        File.WriteAllText(Path.Combine(data, "logs", "cryptochat.db"), "новая база");
        Assert.Null(ConsoleApp.MigrateOldLogs(app, data));
        Assert.Equal("новая база", File.ReadAllText(Path.Combine(data, "logs", "cryptochat.db")));
    }

    [Fact]
    public void NothingToMigrate_WhenNoOldLogsOrSameFolder()
    {
        Assert.Null(ConsoleApp.MigrateOldLogs(_tmp.Sub("app"), _tmp.Sub("data")));
        Directory.CreateDirectory(_tmp.Sub("same", "logs"));
        Assert.Null(ConsoleApp.MigrateOldLogs(_tmp.Sub("same"), _tmp.Sub("same")));
    }

    [Fact]
    public void Option_DataDir() => Assert.True(Options.Parse(new[] { "--data-dir" }).ShowDataDirectory);
}
