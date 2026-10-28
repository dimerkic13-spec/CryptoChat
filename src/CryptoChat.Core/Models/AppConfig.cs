using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoChat.Core.Knowledge;

namespace CryptoChat.Core.Models;

public sealed class ThresholdsConfig
{
    public double Answer { get; set; } = 0.6;
    public double Clarify { get; set; } = 0.3;
    public double ScopeMin { get; set; } = 0.1;
    public double ScopeMargin { get; set; } = 0.05;
    public double DominanceGap { get; set; } = 0.2;
}

public sealed class ScoringConfig
{
    public double ScopeBoost { get; set; } = 0.25;
    public int PrefixMinLength { get; set; } = 4;

    /// <summary>
    /// true: ключи любой категории могут сузить поиск до её ветки.
    /// false: сужают поиск только продукты (kind = "product"), а ключи тем и блоков служат для уточнения по веткам.
    /// </summary>
    public bool CategoryScope { get; set; } = true;
}

public sealed class MenuConfig
{
    public int MaxOptions { get; set; } = 3;
}

public sealed class SupportConfig
{
    public string Url { get; set; } = "support.cryptopro.ru";
}

public sealed class LoggingConfig
{
    public bool Unanswered { get; set; } = false;
    public string Path { get; set; } = "logs/unanswered.jsonl";

    /// <summary>Записывать отзывы «да» / «нет» об ответах.</summary>
    public bool Feedback { get; set; } = false;
    public string FeedbackPath { get; set; } = "logs/feedback.jsonl";
}

/// <summary>Управляемый диалог: уточнение по веткам, отзыв об ответе, перевод в поддержку после нескольких шагов.</summary>
public sealed class DialogConfig
{
    /// <summary>Включает управляемый диалог. Выключено: бот сразу направляет в поддержку, если не уверен.</summary>
    public bool Guided { get; set; } = false;

    /// <summary>Сколько шагов уточнения бот делает, прежде чем направить в поддержку.</summary>
    public int EscalateAfterSteps { get; set; } = 4;

    /// <summary>Сколько веток или вопросов показывать в меню уточнения.</summary>
    public int MaxBranchOptions { get; set; } = 7;

    /// <summary>Минимальная оценка вопроса для списка «Похожие вопросы» после ответа.</summary>
    public double MinScore { get; set; } = 0.05;

    /// <summary>
    /// Если подходящих вопросов нет, вопросы с оценкой по ключам не ниже этой предлагаются
    /// как «Возможно, вы имели в виду…» вместо меню тем (0 — не предлагать).
    /// </summary>
    public double SuggestScore { get; set; } = 0.15;

    /// <summary>Спрашивать после ответа, помог ли он.</summary>
    public bool Feedback { get; set; } = true;
}

public sealed class TextConfig
{
    /// <summary>
    /// Как приводить слово к основе: "stemmer" (простой стеммер), "pymorphy3" (словари морфологии)
    /// или "auto" (морфология, если словари на месте, иначе стеммер).
    /// </summary>
    public string Morphology { get; set; } = "stemmer";
}

/// <summary>Учёт опечаток.</summary>
public sealed class FuzzyConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Минимальная похожесть слов, 0–100.</summary>
    public double Threshold { get; set; } = 85;

    /// <summary>Слова короче не сравниваются нечётко.</summary>
    public int MinLength { get; set; } = 5;

    /// <summary>Какую долю веса даёт ключ, совпавший с опечаткой.</summary>
    public double Weight { get; set; } = 0.8;
}

/// <summary>Поиск по смыслу (необязательно).</summary>
public sealed class SemanticConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Небольшая русская модель (~120 МБ).</summary>
    public string Model { get; set; } = "cointegrated/rubert-tiny2";

    /// <summary>Где лежит модель (рядом с программой).</summary>
    public string CacheDir { get; set; } = "models";

    /// <summary>"cpu" или "cuda".</summary>
    public string Device { get; set; } = "cpu";

    /// <summary>true — не ходить в интернет, брать модель только из CacheDir.</summary>
    public bool LocalFilesOnly { get; set; } = false;

    /// <summary>"relative" (на фоне остальных вопросов) или "absolute".</summary>
    public string Mode { get; set; } = "relative";

    /// <summary>relative: z-оценка ниже — смысловая оценка 0.</summary>
    public double ZLow { get; set; } = 0.0;

    /// <summary>relative: z-оценка выше — смысловая оценка = Weight.</summary>
    public double ZHigh { get; set; } = 5.0;

    /// <summary>relative: сколько самых похожих вопросов учитывать.</summary>
    public int TopK { get; set; } = 3;

    /// <summary>absolute: похожесть ниже — смысловая оценка 0.</summary>
    public double Low { get; set; } = 0.55;

    /// <summary>absolute: похожесть выше — смысловая оценка = Weight.</summary>
    public double High { get; set; } = 0.9;

    /// <summary>Максимальная оценка от смысла.</summary>
    public double Weight { get; set; } = 0.3;

    /// <summary>"or" (смысл усиливает ключи) или "max".</summary>
    public string Combine { get; set; } = "or";

    /// <summary>
    /// Запасное меню: если ключи не нашли ничего, показать самые похожие по смыслу вопросы
    /// вместо меню тем. Вопрос попадает в меню, если его z-оценка не ниже MenuZ.
    /// </summary>
    public bool MenuFallback { get; set; } = true;
    public double MenuZ { get; set; } = 1.5;
    public int MenuSize { get; set; } = 3;

    /// <summary>Для моделей e5/USER: "query: ".</summary>
    public string QueryPrefix { get; set; } = "";

    /// <summary>Для моделей e5/USER: "passage: ".</summary>
    public string PassagePrefix { get; set; } = "";
}

/// <summary>Журнал каждой беседы в отдельном файле с номером сессии.</summary>
public sealed class SessionsConfig
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "logs/sessions";
}

/// <summary>База бесед SQLite: сессии, сообщения, отзывы, неотвеченные запросы (для --stats).</summary>
public sealed class DatabaseConfig
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "logs/cryptochat.db";
}

/// <summary>Отправка журнала беседы на почту после закрытия программы.</summary>
public sealed class EmailConfig
{
    public bool Enabled { get; set; } = false;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 465;

    /// <summary>"ssl" (порт 465), "starttls" (порт 587) или "none".</summary>
    public string Security { get; set; } = "ssl";

    /// <summary>Логин ящика-отправителя.</summary>
    public string Username { get; set; } = "";

    /// <summary>Адрес «От кого»; пусто — берётся Username.</summary>
    [JsonPropertyName("from")]
    public string Sender { get; set; } = "";

    /// <summary>Кому: список адресов или одна строка через запятую.</summary>
    [JsonConverter(typeof(StringOrListConverter))]
    public List<string> To { get; set; } = new();

    public string SubjectPrefix { get; set; } = "CryptoChat";

    /// <summary>Переменная окружения с паролем.</summary>
    public string PasswordEnv { get; set; } = "CRYPTOCHAT_SMTP_PASSWORD";

    /// <summary>Отправлять ли беседы без единого вопроса.</summary>
    public bool SendEmpty { get; set; } = false;

    /// <summary>При запуске досылать то, что не ушло в прошлый раз.</summary>
    public bool SendPendingOnStart { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Адреса получателей. Каждый элемент может содержать несколько адресов через запятую или точку с запятой.</summary>
    public IReadOnlyList<string> Recipients() =>
        To.SelectMany(item => item.Replace(';', ',').Split(','))
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .ToList();

    public string FromAddress() => (Sender.Length > 0 ? Sender : Username).Trim();
}

/// <summary>Параметры поведения бота. Значения по умолчанию заданы здесь, файл config.json их переопределяет.</summary>
public sealed class AppConfig
{
    public ThresholdsConfig Thresholds { get; set; } = new();
    public ScoringConfig Scoring { get; set; } = new();
    public MenuConfig Menu { get; set; } = new();
    public SupportConfig Support { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public DialogConfig Dialog { get; set; } = new();
    public TextConfig Text { get; set; } = new();
    public FuzzyConfig Fuzzy { get; set; } = new();
    public SemanticConfig Semantic { get; set; } = new();
    public SessionsConfig Sessions { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public EmailConfig Email { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static AppConfig Load(string directory)
    {
        var file = System.IO.Path.Combine(directory, "config.json");
        if (!File.Exists(file))
            return new AppConfig();

        AppConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(file, Encoding.UTF8), JsonOptions)
                     ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new ContentException($"Ошибка в файле {file}: {ex.Message}", ex);
        }
        config.Validate(file);
        return config;
    }

    public void Validate(string source = "config.json")
    {
        void Require(bool condition, string message)
        {
            if (!condition)
                throw new ContentException($"{source}: {message}");
        }

        var t = Thresholds;
        Require(!(t.Clarify <= 0 || t.Answer > 1 || t.Clarify > t.Answer), "должно выполняться 0 < clarify <= answer <= 1.");
        Require(Menu.MaxOptions >= 1, "menu.maxOptions должно быть не меньше 1.");
        Require(Dialog.EscalateAfterSteps >= 1, "dialog.escalateAfterSteps должно быть не меньше 1.");
        Require(Dialog.MaxBranchOptions >= 2, "dialog.maxBranchOptions должно быть не меньше 2.");
        Require(Dialog.SuggestScore is >= 0 and <= 1, "dialog.suggestScore должно быть от 0 до 1.");
        Require(Dialog.MinScore is >= 0 and <= 1, "dialog.minScore должно быть от 0 до 1.");
        Require(Text.Morphology.ToLowerInvariant() is "stemmer" or "pymorphy3" or "auto",
            "text.morphology должно быть stemmer, pymorphy3 или auto.");

        var s = Semantic;
        Require(-1 <= s.Low && s.Low < s.High && s.High <= 1, "semantic.low и semantic.high — от -1 до 1, low меньше high.");
        Require(s.Weight > 0 && s.Weight <= 1, "semantic.weight должно быть больше 0 и не больше 1.");
        Require(s.Mode is "relative" or "absolute", "semantic.mode должно быть relative или absolute.");
        Require(s.Combine is "max" or "or", "semantic.combine должно быть max или or.");
        Require(s.ZLow < s.ZHigh, "semantic.zLow должно быть меньше semantic.zHigh.");
        Require(s.MenuSize >= 1, "semantic.menuSize должно быть не меньше 1.");
        Require(s.TopK >= 1, "semantic.topK должно быть не меньше 1.");
        Require(!s.Enabled || s.Model.Trim().Length > 0, "semantic.model не задан.");

        Require(Fuzzy.Threshold is >= 50 and <= 100, "fuzzy.threshold должно быть от 50 до 100.");
        Require(Fuzzy.Weight > 0 && Fuzzy.Weight <= 1, "fuzzy.weight должно быть больше 0 и не больше 1.");
        Require(Fuzzy.MinLength >= 3, "fuzzy.minLength должно быть не меньше 3.");

        Require(Email.Security.ToLowerInvariant() is "ssl" or "starttls" or "none",
            "email.security должно быть ssl, starttls или none.");
        if (Email.Enabled)
        {
            Require(Email.SmtpHost.Trim().Length > 0, "email.smtpHost не задан (например smtp.yandex.ru).");
            Require(Email.FromAddress().Length > 0, "email.username или email.sender не задан.");
            Require(Email.Recipients().Count > 0, "email.to не задан — укажите, кому отправлять журнал.");
        }

        Require(Scoring.PrefixMinLength >= 1, "scoring.prefixMinLength должно быть не меньше 1.");
    }
}

/// <summary>Читает и строку "a@b.ru", и список ["a@b.ru", "c@d.ru"].</summary>
internal sealed class StringOrListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new List<string> { reader.GetString() ?? "" };
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Ожидался список строк [\"...\"].");

        var result = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Ожидался список строк [\"...\"].");
            result.Add(reader.GetString() ?? "");
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
