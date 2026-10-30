using System.Text;
using CryptoChat.Cli;

namespace CryptoChat.Tests;

/// <summary>0.3.1a: publish\CryptoChat.exe при запуске подтягивает изменённую базу знаний из папки проекта.</summary>
public class ContentSyncTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private string Project => _tmp.Sub("project", "content");
    private string App => _tmp.Sub("publish");
    private string Target => Path.Combine(App, "content");

    public ContentSyncTests()
    {
        Copy(TestSupport.FindContentDir(), Project);
        Copy(TestSupport.FindContentDir(), Target);
        File.WriteAllText(Path.Combine(App, ContentSync.SourceFile), Project + Environment.NewLine, new UTF8Encoding(true));
    }

    public void Dispose() => _tmp.Dispose();

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private string QuestionsFile(string root) => Path.Combine(root, "questions", "1_commercial.json");

    [Fact]
    public void NothingChanged_NothingHappens()
    {
        var result = ContentSync.Run(App);
        Assert.False(result.Updated);
        Assert.Null(result.Message);
    }

    [Fact]
    public void ChangedAddedAndDeletedFiles_AreMirrored()
    {
        var text = File.ReadAllText(QuestionsFile(Project), Encoding.UTF8);
        File.WriteAllText(QuestionsFile(Project), text.Replace("Как продлить лицензию?", "Как продлить лицензию? (правка)"), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Project, "notes.txt"), "новый файл");
        File.WriteAllText(Path.Combine(Target, "stale.json"), "лишний файл старой базы");

        var result = ContentSync.Run(App);

        Assert.True(result.Updated);
        Assert.Contains("изменено файлов: 3", result.Message);
        Assert.Contains("(правка)", File.ReadAllText(QuestionsFile(Target), Encoding.UTF8));
        Assert.True(File.Exists(Path.Combine(Target, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(Target, "stale.json")));
        Assert.Equal(0, ContentSync.Differences(Project, Target));
        Assert.False(Directory.Exists(Target + ".new"));
        Assert.False(Directory.Exists(Target + ".old"));
    }

    [Fact]
    public void BrokenProjectBase_IsNotCopied()
    {
        var before = File.ReadAllText(QuestionsFile(Target), Encoding.UTF8);
        File.WriteAllText(QuestionsFile(Project), "[ это не JSON", new UTF8Encoding(false));

        var result = ContentSync.Run(App);

        Assert.False(result.Updated);
        Assert.Contains("ошибка", result.Message);
        Assert.Contains("прежней базе", result.Message);
        Assert.Equal(before, File.ReadAllText(QuestionsFile(Target), Encoding.UTF8));
    }

    [Fact]
    public void ProjectFolderMissing_SilentlyKeepsOwnBase()
    {
        File.WriteAllText(Path.Combine(App, ContentSync.SourceFile), _tmp.Sub("нет-такой-папки"));
        File.WriteAllText(Path.Combine(Target, "own.txt"), "своя");

        var result = ContentSync.Run(App);

        Assert.Equal((false, (string?)null), (result.Updated, result.Message));
        Assert.True(File.Exists(Path.Combine(Target, "own.txt")));
    }

    [Fact]
    public void WithoutMarkerFile_NothingHappens()
    {
        File.Delete(Path.Combine(App, ContentSync.SourceFile));
        File.WriteAllText(Path.Combine(Project, "notes.txt"), "новый файл");

        Assert.Null(ContentSync.Run(App).Message);
        Assert.False(File.Exists(Path.Combine(Target, "notes.txt")));
    }

    [Fact]
    public void MarkerPointingToOwnContent_DoesNothing()
    {
        File.WriteAllText(Path.Combine(App, ContentSync.SourceFile), "\"" + Target + "\"");
        Assert.Null(ContentSync.Run(App).Message);
    }
}
