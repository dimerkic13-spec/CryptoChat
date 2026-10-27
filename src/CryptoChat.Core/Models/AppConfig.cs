using System.Text;
using System.Text.Json;
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

    /// <summary>Спрашивать после ответа, помог ли он.</summary>
    public bool Feedback { get; set; } = true;
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static AppConfig Load(string directory)
    {
        var file = System.IO.Path.Combine(directory, "config.json");
        if (!File.Exists(file))
            return new AppConfig();

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(file, Encoding.UTF8), JsonOptions)
                         ?? new AppConfig();
            config.Validate(file);
            return config;
        }
        catch (JsonException ex)
        {
            throw new ContentException($"Ошибка в файле {file}: {ex.Message}", ex);
        }
    }

    public void Validate(string source = "config.json")
    {
        if (Thresholds.Clarify <= 0 || Thresholds.Answer > 1 || Thresholds.Clarify > Thresholds.Answer)
            throw new ContentException($"{source}: должно выполняться 0 < clarify <= answer <= 1.");
        if (Menu.MaxOptions < 1)
            throw new ContentException($"{source}: menu.maxOptions должно быть не меньше 1.");
        if (Dialog.EscalateAfterSteps < 1)
            throw new ContentException($"{source}: dialog.escalateAfterSteps должно быть не меньше 1.");
        if (Dialog.MaxBranchOptions < 2)
            throw new ContentException($"{source}: dialog.maxBranchOptions должно быть не меньше 2.");
        if (Dialog.MinScore < 0 || Dialog.MinScore > 1)
            throw new ContentException($"{source}: dialog.minScore должно быть от 0 до 1.");
        if (Scoring.PrefixMinLength < 1)
            throw new ContentException($"{source}: scoring.prefixMinLength должно быть не меньше 1.");
    }
}
