using System.Reflection;
using System.Text;
using CryptoChat.Core;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Semantic;

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
                return Validate(contentDir, options.Morphology);
            if (options.Compare)
                return Compare.Run(contentDir);
            if (options.CompareSemantic)
                return SemanticCommands.CompareSemantic(contentDir, options.Morphology);
            if (options.DownloadModel)
                return SemanticCommands.DownloadModel(contentDir);
            if (options.CheckModel)
                return SemanticCommands.CheckModel(contentDir);
            if (options.SemanticDump)
                return SemanticCommands.SemanticDump(contentDir, options.Morphology);
            if (options.TestEmail || options.SendPending)
                return EmailCommand(contentDir, options);
            if (options.Stats)
                return Stats(contentDir);

            var semantic = AppConfig.Load(contentDir).Semantic;
            if (semantic.Enabled && SemanticFactory.ModelExists(semantic, AppDirectory()))
                Console.WriteLine("Загружаю модель для поиска по смыслу...");
            var engine = ChatEngine.Create(contentDir, options.Morphology, appDir: AppDirectory());
            if (engine.SemanticNote != null)
                Console.WriteLine("Поиск по смыслу недоступен: " + engine.SemanticNote);
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
        var conversation = new Conversation(engine);
        if (engine.Config.Email.Enabled && engine.Config.Email.SendPendingOnStart)
            SendLogs(engine.Config, skip: conversation.SessionId, db: conversation.Database);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Бот: " + Goodbye);
            conversation.Close();
            Environment.Exit(0);
        };

        try
        {
            return ChatLoop(engine, conversation);
        }
        finally
        {
            conversation.Close();
        }
    }

    private static int ChatLoop(ChatEngine engine, Conversation conversation)
    {
        var kb = engine.KnowledgeBase;
        Console.WriteLine($"CryptoChat {Version()}");
        var parts = kb.Roots.Select(r => $"{r.Name}: {kb.QuestionsUnder(r.Id).Count()}");
        Console.WriteLine($"Загружено тем: {kb.Roots.Count}, вопросов: {kb.Questions.Count} ({string.Join("; ", parts)})");
        var note = engine.MorphologyNote != null ? $" ({engine.MorphologyNote})" : "";
        Console.WriteLine($"Морфология: {engine.Morphology}{note}");
        Console.WriteLine($"Учёт опечаток: {engine.Fuzzy?.Name ?? "выключен"}");
        Console.WriteLine(engine.Semantic != null
            ? $"Поиск по смыслу: {engine.Semantic.Name} ({engine.Semantic.Phrases} фраз)"
            : "Поиск по смыслу: выключен" + (engine.SemanticNote != null ? " (недоступен)" : ""));
        if (engine.SessionId != null)
            Console.WriteLine($"Номер сессии: {engine.SessionId}");
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

            conversation.User(input);
            var reply = engine.Ask(input);
            conversation.Bot(input, reply);
            Print(reply);

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

    private static int Validate(string contentDir, string? morphology)
    {
        var config = AppConfig.Load(contentDir);
        var (lemmatizer, note) = ChatEngine.CreateLemmatizer(morphology ?? config.Text.Morphology);
        var content = KnowledgeLoader.LoadContent(contentDir);
        var issues = ContentValidator.Validate(content, lemmatizer: lemmatizer);

        Console.WriteLine($"Каталог с данными: {contentDir}");
        Console.WriteLine($"Морфология: {lemmatizer.Name}" + (note != null ? $" ({note})" : ""));
        Console.WriteLine($"Вопросов: {content.Questions.Count}");
        foreach (var issue in issues)
            Console.WriteLine("  " + issue);

        var errors = issues.Count(i => i.Severity == Severity.Error);
        var warnings = issues.Count - errors;
        Console.WriteLine($"Ошибок: {errors}, предупреждений: {warnings}");
        return errors == 0 ? 0 : 1;
    }

    // ---------- Статистика ----------

    private static int Stats(string contentDir)
    {
        var config = AppConfig.Load(contentDir);
        var titles = new Dictionary<string, string>();
        try
        {
            foreach (var q in KnowledgeLoader.LoadContent(contentDir).Questions)
                titles.TryAdd(q.Id, q.Title);
        }
        catch (ContentException)
        {
            // Статистика нужна и тогда, когда в базе знаний ошибка.
        }
        Console.WriteLine(ChatDatabase.StatsReport(DatabasePath(config), titles));
        return 0;
    }

    // ---------- Почта ----------

    private static int EmailCommand(string contentDir, Options options)
    {
        var config = AppConfig.Load(contentDir);
        var email = config.Email;
        if (!email.Enabled)
        {
            Console.WriteLine("Отправка на почту выключена: в config.json поставьте \"email\": {\"enabled\": true, ...}.");
            return 1;
        }
        try
        {
            if (options.TestEmail)
            {
                Mailer.SendTest(email, AppDirectory());
                Console.WriteLine($"Тестовое письмо отправлено на {string.Join(", ", email.Recipients())}.");
            }
            if (options.SendPending)
            {
                var sent = Mailer.SendPending(email, AppDirectory(), SessionsFolder(config));
                Console.WriteLine($"Отправлено журналов: {sent}.");
            }
        }
        catch (MailException ex)
        {
            Console.WriteLine("Ошибка: " + ex.Message);
            return 1;
        }
        return 0;
    }

    /// <summary>Досылает готовые журналы на почту. Ошибка отправки не мешает работе: журнал уйдёт в следующий раз.</summary>
    internal static void SendLogs(AppConfig config, string? skip = null, ChatDatabase? db = null)
    {
        if (!(config.Email.Enabled && config.Sessions.Enabled))
            return;
        try
        {
            Mailer.SendPending(config.Email, AppDirectory(), SessionsFolder(config), skip,
                onSent: db != null ? db.MarkEmailed : null);
        }
        catch (MailException ex)
        {
            Console.WriteLine($"Журнал не отправлен: {ex.Message}");
            Console.WriteLine("Он сохранён и будет отправлен при следующем запуске.");
        }
    }

    // ---------- Где лежит программа и данные ----------

    /// <summary>Папка программы: рядом с CryptoChat.exe.</summary>
    internal static string AppDirectory() => AppContext.BaseDirectory;

    internal static string LogPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppDirectory(), path);

    internal static string SessionsFolder(AppConfig config) => LogPath(config.Sessions.Path);

    internal static string DatabasePath(AppConfig config) => LogPath(config.Database.Path);

    private static string ResolveContentDirectory(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.ContentPath))
            return Path.GetFullPath(options.ContentPath);

        var fromEnv = Environment.GetEnvironmentVariable("CRYPTOCHAT_CONTENT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        var besideProgram = Path.Combine(AppDirectory(), "content");
        if (Directory.Exists(besideProgram))
            return besideProgram;

        var inCurrent = Path.Combine(Directory.GetCurrentDirectory(), "content");
        return Directory.Exists(inCurrent) ? inCurrent : besideProgram;
    }

    internal static string Version()
    {
        var info = typeof(ConsoleApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.2.5a";
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
        Console.WriteLine("  --compare          сравнить стеммер и pymorphy3 на приёмочных вопросах");
        Console.WriteLine("  --morphology <m>   stemmer, pymorphy3 или auto (вместо text.morphology из config.json)");
        Console.WriteLine("  --compare-semantic сравнить поиск с моделью и без неё (контрольные запросы и перефразы)");
        Console.WriteLine("  --check-model      проверить модель для поиска по смыслу (сверка с Python-версией)");
        Console.WriteLine("  --download-model   как получить модель для поиска по смыслу");
        Console.WriteLine("  --semantic-dump    выгрузить оценки модели в semantic_dump.json (для настройки порогов)");
        Console.WriteLine("  --debug            показывать, как бот ищет ответ");
        Console.WriteLine("  --test-email       отправить тестовое письмо по настройкам email из config.json");
        Console.WriteLine("  --send-pending     отправить на почту журналы, которые не ушли раньше");
        Console.WriteLine("  --stats            статистика по базе бесед: частые вопросы, отзывы, неотвеченные запросы");
        Console.WriteLine("  --version          показать версию");
        Console.WriteLine("  --help             эта подсказка");
    }
}
