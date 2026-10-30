using System.Globalization;

namespace CryptoChat.Cli;

/// <summary>Ключи запуска программы.</summary>
internal sealed class Options
{
    public string? ContentPath { get; private set; }
    public bool Validate { get; private set; }
    public bool SelfTest { get; private set; }
    public bool Synthetic { get; private set; }
    public bool Compare { get; private set; }
    public bool CompareSemantic { get; private set; }
    public bool DownloadModel { get; private set; }
    public bool CheckModel { get; private set; }
    public bool SemanticDump { get; private set; }
    public string? Morphology { get; private set; }
    public bool Debug { get; private set; }
    public bool TestEmail { get; private set; }
    public bool SendPending { get; private set; }
    public bool Stats { get; private set; }
    public bool StatsCsv { get; private set; }
    public string? StatsCsvFolder { get; private set; }
    public bool Sessions { get; private set; }
    public bool ShowDataDirectory { get; private set; }
    public string? Session { get; private set; }
    public DateOnly? From { get; private set; }
    public DateOnly? To { get; private set; }
    public int Top { get; private set; } = 10;
    public bool ShowVersion { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Error { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length && options.Error == null; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--content":
                    if (i + 1 >= args.Length)
                        options.Error = "После --content нужно указать путь к каталогу.";
                    else
                        options.ContentPath = args[++i];
                    break;
                case "--morphology":
                    if (i + 1 >= args.Length)
                        options.Error = "После --morphology нужно указать stemmer, pymorphy3 или auto.";
                    else
                        options.Morphology = args[++i].ToLowerInvariant();
                    break;
                case "--validate": options.Validate = true; break;
                case "--selftest": options.SelfTest = true; break;
                case "--synthetic": options.Synthetic = true; break;
                case "--compare": options.Compare = true; break;
                case "--compare-semantic": options.CompareSemantic = true; break;
                case "--download-model": options.DownloadModel = true; break;
                case "--check-model": options.CheckModel = true; break;
                case "--semantic-dump": options.SemanticDump = true; break;
                case "--debug": options.Debug = true; break;
                case "--test-email": options.TestEmail = true; break;
                case "--send-pending": options.SendPending = true; break;
                case "--stats": options.Stats = true; break;
                case "--stats-csv":
                    options.StatsCsv = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        options.StatsCsvFolder = args[++i];
                    break;
                case "--sessions": options.Sessions = true; break;
                case "--data-dir": options.ShowDataDirectory = true; break;
                case "--session":
                    if (i + 1 >= args.Length)
                        options.Error = "После --session нужно указать номер сессии (список: --sessions).";
                    else
                        options.Session = args[++i];
                    break;
                case "--from":
                case "--to":
                    var key = args[i].ToLowerInvariant();
                    if (i + 1 >= args.Length || ParseDate(args[i + 1]) is not { } date)
                        options.Error = $"После {key} нужна дата: 2026-10-29 или 29.10.2026.";
                    else
                    {
                        i++;
                        if (key == "--from") options.From = date;
                        else options.To = date;
                    }
                    break;
                case "--top":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var top) || top < 1)
                        options.Error = "После --top нужно число больше 0, например --top 20.";
                    else
                    {
                        i++;
                        options.Top = top;
                    }
                    break;
                case "--version": options.ShowVersion = true; break;
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                default:
                    options.Error = $"Неизвестный ключ: {args[i]}";
                    break;
            }
        }
        return options;
    }

    private static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text, new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
