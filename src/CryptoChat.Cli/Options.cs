namespace CryptoChat.Cli;

/// <summary>Ключи запуска программы.</summary>
internal sealed class Options
{
    public string? ContentPath { get; private set; }
    public bool Validate { get; private set; }
    public bool SelfTest { get; private set; }
    public bool Debug { get; private set; }
    public bool ShowVersion { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Error { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--content":
                    if (i + 1 >= args.Length)
                        options.Error = "После --content нужно указать путь к каталогу.";
                    else
                        options.ContentPath = args[++i];
                    break;
                case "--validate": options.Validate = true; break;
                case "--selftest": options.SelfTest = true; break;
                case "--debug": options.Debug = true; break;
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
}
