using System.Text;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>
/// Автоматическое обновление базы знаний собранной программы из папки проекта.
///
/// build_exe.ps1 кладёт рядом с CryptoChat.exe файл content-source.txt с путём к папке content проекта.
/// При каждом запуске программа сравнивает свою папку content с папкой проекта и, если там что-то изменилось,
/// проверяет новую базу и копирует её к себе. База с ошибками не копируется — бот работает на прежней.
/// На другом компьютере (папки проекта нет) и в установленной программе (файла нет) ничего не происходит.
/// </summary>
internal static class ContentSync
{
    public const string SourceFile = "content-source.txt";

    /// <summary>Чем закончилась проверка: Message — что сказать пользователю (null — молчать).</summary>
    public sealed record Result(bool Updated, string? Message);

    private static readonly Result Nothing = new(false, null);

    public static Result Run(string appDir)
    {
        var marker = Path.Combine(appDir, SourceFile);
        if (!File.Exists(marker))
            return Nothing;

        string source;
        try
        {
            source = File.ReadAllText(marker, Encoding.UTF8).Trim().Trim('"');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Nothing;
        }
        var target = Path.Combine(appDir, "content");
        if (source.Length == 0 || !Directory.Exists(source))
            return Nothing; // другой компьютер: папки проекта здесь нет — работаем на своей базе
        if (Same(Path.GetFullPath(source), Path.GetFullPath(target)))
            return Nothing;

        var changed = Differences(source, target);
        if (changed == 0)
            return Nothing;

        // Сначала проверяем новую базу: с ошибкой бот не запустился бы.
        try
        {
            var config = AppConfig.Load(source);
            var (lemmatizer, _) = ChatEngine.CreateLemmatizer(config.Text.Morphology);
            KnowledgeLoader.Load(source, lemmatizer);
        }
        catch (Exception ex) when (ex is ContentException or IOException or UnauthorizedAccessException)
        {
            return new Result(false,
                $"База знаний в {source} изменена, но в ней ошибка — бот работает на прежней базе ({target}).\n" +
                $"  {ex.Message.Replace("\n", "\n  ")}\n" +
                "  Исправьте ошибку и запустите программу снова (проверка: CryptoChat.exe --validate --content <папка>).");
        }

        // Копируем во временную папку и только потом подменяем — чтобы при сбое не остаться без базы.
        var fresh = target + ".new";
        var old = target + ".old";
        try
        {
            DeleteIfExists(fresh);
            DeleteIfExists(old);
            CopyFolder(source, fresh);
            if (Directory.Exists(target))
                Directory.Move(target, old);
            Directory.Move(fresh, target);
            DeleteIfExists(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(target) && Directory.Exists(old))
                Directory.Move(old, target); // вернуть прежнюю базу
            return new Result(false, $"Не удалось обновить базу знаний из {source}: {ex.Message}. Бот работает на прежней базе.");
        }
        return new Result(true, $"База знаний обновлена из {source} (изменено файлов: {changed}).");
    }

    /// <summary>Сколько файлов отличается: новые, изменённые и удалённые.</summary>
    public static int Differences(string source, string target)
    {
        var a = Files(source);
        var b = Files(target);
        var changed = a.Keys.Count(k => !b.ContainsKey(k) || !SameFile(a[k], b[k]));
        return changed + b.Keys.Count(k => !a.ContainsKey(k));
    }

    private static Dictionary<string, string> Files(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), f => f, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
        Directory.CreateDirectory(to); // пустая папка проекта — тоже папка
    }

    private static void DeleteIfExists(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}
