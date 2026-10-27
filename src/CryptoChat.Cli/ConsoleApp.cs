using System.Reflection;
using System.Text;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;

namespace CryptoChat.Cli;

/// <summary>Консольный интерфейс: только ввод и вывод. Вся логика живёт в CryptoChat.Core.</summary>
public static class ConsoleApp
{
    private const string Goodbye = "До свидания!";

    public static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Ввод или вывод перенаправлен: кодировку менять не нужно.
        }

        var options = Options.Parse(args);
        if (options.Error != null)
        {
            Console.Error.WriteLine(options.Error);
            PrintUsage();
            return 2;
        }
        if (options.ShowVersion)
        {
            Console.WriteLine($"CryptoChat {Version()}");
            return 0;
        }
        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        var contentDir = ResolveContentDirectory(options);
        try
        {
            if (options.Validate)
                return Validate(contentDir);

            var engine = ChatEngine.Create(contentDir);
            engine.DebugEnabled = options.Debug;

            if (options.SelfTest)
                return SelfTest.Run(engine, contentDir);

            return Chat(engine);
        }
        catch (ContentException ex)
        {
            Console.Error.WriteLine("Ошибка: " + ex.Message);
            Console.Error.WriteLine($"Каталог с данными: {contentDir}");
            return 2;
        }
    }

    // ---------- Диалог ----------

    private static int Chat(ChatEngine engine)
    {
        var kb = engine.KnowledgeBase;
        var log = new UnansweredLog(engine.Config);
        var feedbackLog = new FeedbackLog(engine.Config);
        engine.FeedbackGiven += feedbackLog.Write;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Бот: " + Goodbye);
            Environment.Exit(0);
        };

        Console.WriteLine($"CryptoChat {Version()}");
        var parts = kb.Roots.Select(r => $"{r.Name}: {kb.QuestionsUnder(r.Id).Count()}");
        Console.WriteLine($"Загружено тем: {kb.Roots.Count}, вопросов: {kb.Questions.Count} ({string.Join("; ", parts)})");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("  Помощник КриптоПро");
        Console.WriteLine("  Задайте вопрос обычным текстом. Подсказка: /помощь. Выход: выход");
        Console.WriteLine(new string('=', 60));

        bool awaitingChoice = false;
        while (true)
        {
            Console.Write(awaitingChoice ? "\nВаш выбор: " : "\nВы: ");
            var input = Console.ReadLine();
            if (input == null) // конец ввода
            {
                Console.WriteLine();
                Console.WriteLine("Бот: " + Goodbye);
                return 0;
            }
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var reply = engine.Ask(input);
            Print(reply);
            if (!int.TryParse(input.Trim(), out _))
                log.Write(input, reply.Kind);

            if (reply.Kind == ReplyKind.Exit)
                return 0;
            awaitingChoice = reply.Kind is ReplyKind.Clarify or ReplyKind.ClarifyProduct or ReplyKind.ClarifyBranch;
        }
    }

    private static void Print(ChatReply reply)
    {
        if (reply.Debug != null)
        {
            Console.WriteLine();
            foreach (var line in reply.Debug.Split('\n'))
                Console.WriteLine(line);
        }

        var lines = reply.Text.Split('\n');
        Console.WriteLine();
        Console.WriteLine("Бот: " + lines[0]);
        foreach (var line in lines.Skip(1))
            Console.WriteLine("     " + line);
    }

    // ---------- Проверка данных ----------

    private static int Validate(string contentDir)
    {
        var content = KnowledgeLoader.LoadContent(contentDir);
        var issues = ContentValidator.Validate(content);

        Console.WriteLine($"Каталог с данными: {contentDir}");
        Console.WriteLine($"Вопросов: {content.Questions.Count}");
        foreach (var issue in issues)
            Console.WriteLine("  " + issue);

        var errors = issues.Count(i => i.Severity == Severity.Error);
        var warnings = issues.Count - errors;
        Console.WriteLine($"Ошибок: {errors}, предупреждений: {warnings}");
        return errors == 0 ? 0 : 1;
    }

    // ---------- Служебное ----------

    private static string ResolveContentDirectory(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.ContentPath))
            return Path.GetFullPath(options.ContentPath);

        var fromEnv = Environment.GetEnvironmentVariable("CRYPTOCHAT_CONTENT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        var besideProgram = Path.Combine(AppContext.BaseDirectory, "content");
        if (Directory.Exists(besideProgram))
            return besideProgram;

        var inCurrent = Path.Combine(Directory.GetCurrentDirectory(), "content");
        return Directory.Exists(inCurrent) ? inCurrent : besideProgram;
    }

    private static string Version()
    {
        var info = typeof(ConsoleApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1a";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("CryptoChat — помощник по вопросам КриптоПро");
        Console.WriteLine();
        Console.WriteLine("Запуск:  CryptoChat [ключи]");
        Console.WriteLine("  --content <путь>   каталог с данными (иначе ./content рядом с программой)");
        Console.WriteLine("  --validate         проверить данные и выйти");
        Console.WriteLine("  --selftest         прогнать приёмочные вопросы и выйти");
        Console.WriteLine("  --debug            показывать, как бот ищет ответ");
        Console.WriteLine("  --version          показать версию");
        Console.WriteLine("  --help             эта подсказка");
    }
}
