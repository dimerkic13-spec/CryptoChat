using System.Text;
using CryptoChat.Cli;
using CryptoChat.Core;
using CryptoChat.Core.Models;

namespace CryptoChat.Tests;

/// <summary>Статистика по базе бесед: популярные и редкие вопросы, сессии, выгрузка в CSV, итог беседы в журнале.</summary>
public class AnalyticsTests : IDisposable
{
    private readonly TempFolder _tmp = new();
    private string DbPath => _tmp.Sub("logs", "cryptochat.db");

    public void Dispose() => _tmp.Dispose();

    private static readonly Analytics.Catalog Catalog = new(
        new[]
        {
            new Analytics.CatalogQuestion("1.1.1.1", "Какую лицензию выбрать?", "1.1.1"),
            new Analytics.CatalogQuestion("1.1.1.2", "Что нужно для ФНС?", "1.1.1"),
            new Analytics.CatalogQuestion("1.2.1.1", "Как получить счёт?", "1.2.1"),
            new Analytics.CatalogQuestion("1.3.3.2", "Забыл пароль от кабинета", "1.3.3"),
        },
        new Dictionary<string, string> { ["1.1.1"] = "Выбор лицензии", ["1.2.1"] = "Счёт", ["1.3.3"] = "Кабинет" });

    private static Question Q(string id) => new(id, id[..id.LastIndexOf('.')], id, "ответ", "", null, null, true,
        Array.Empty<Keyword>(), Array.Empty<string>());

    private static ChatReply Answer(string id) => new(ReplyKind.Answer, "Ответ " + id, Array.Empty<ChatOption>(), null, Q(id), 0.8);

    private static ChatReply Menu() => new(ReplyKind.Clarify, "Уточните", Array.Empty<ChatOption>());

    /// <summary>Две беседы: счёт спрашивают трижды (дважды через меню), выбор лицензии — один раз, ФНС и кабинет — ни разу.</summary>
    private void Fill()
    {
        using var db = new ChatDatabase(DbPath);
        db.StartSession("s1", "pc1", "0.2.9a", "pymorphy3");
        db.UserMessage("s1", "как получить счёт");
        db.BotMessage("s1", Answer("1.2.1.1"));
        db.UserMessage("s1", "да");
        db.BotMessage("s1", new ChatReply(ReplyKind.Feedback, "Спасибо за отзыв!", Array.Empty<ChatOption>()));
        db.Feedback("s1", new FeedbackEntry(DateTime.Now, "как получить счёт", "1.2.1.1", true));
        db.UserMessage("s1", "счёт");
        db.BotMessage("s1", Menu());
        db.UserMessage("s1", "1");
        db.BotMessage("s1", Answer("1.2.1.1"));
        db.UserMessage("s1", "абракадабра");
        db.BotMessage("s1", new ChatReply(ReplyKind.ClarifyBranch, "Уточните тему", Array.Empty<ChatOption>()));
        db.Unanswered("s1", "абракадабра", ReplyKind.ClarifyBranch);
        db.FinishSession("s1");

        db.StartSession("s2", "pc2", "0.2.9a", "pymorphy3");
        db.UserMessage("s2", "какую лицензию брать");
        db.BotMessage("s2", Menu());
        db.UserMessage("s2", "2");
        db.BotMessage("s2", Answer("1.1.1.1"));
        db.UserMessage("s2", "нет");
        db.BotMessage("s2", new ChatReply(ReplyKind.Feedback, "Жаль", Array.Empty<ChatOption>()));
        db.Feedback("s2", new FeedbackEntry(DateTime.Now, "какую лицензию брать", "1.1.1.1", false));
        db.UserMessage("s2", "счёт на оплату");
        db.BotMessage("s2", Menu());
        db.UserMessage("s2", "1");
        db.BotMessage("s2", Answer("1.2.1.1"));
        db.UserMessage("s2", "Абракадабра ");
        db.BotMessage("s2", new ChatReply(ReplyKind.Escalate, "В поддержку", Array.Empty<ChatOption>()));
        db.Unanswered("s2", "Абракадабра ", ReplyKind.Escalate);
        db.FinishSession("s2");
    }

    [Fact]
    public void Questions_CountDirectAndMenuAnswersAndFeedback()
    {
        Fill();
        var stats = Analytics.Questions(Analytics.Load(DbPath, Analytics.Period.All), Catalog).ToDictionary(q => q.Id);

        Assert.Equal((3, 1, 2, 1, 0), (stats["1.2.1.1"].Answers, stats["1.2.1.1"].Direct, stats["1.2.1.1"].ViaMenu,
            stats["1.2.1.1"].Helpful, stats["1.2.1.1"].Unhelpful));
        Assert.Equal((1, 0, 1, 0, 1), (stats["1.1.1.1"].Answers, stats["1.1.1.1"].Direct, stats["1.1.1.1"].ViaMenu,
            stats["1.1.1.1"].Helpful, stats["1.1.1.1"].Unhelpful));
        Assert.Equal(0, stats["1.1.1.2"].Answers);
    }

    [Fact]
    public void Sessions_CountQuestionsWithoutMenuNumbersAndFeedbackWords()
    {
        Fill();
        var sessions = Analytics.Sessions(Analytics.Load(DbPath, Analytics.Period.All)).ToDictionary(s => s.Id);

        // s1: «как получить счёт», «счёт», «абракадабра» — «да» (отзыв) и «1» (номер пункта) не вопросы
        Assert.Equal((3, 2, 1, 1, 1, 0), (sessions["s1"].Questions, sessions["s1"].Answers, sessions["s1"].ViaMenu,
            sessions["s1"].Unanswered, sessions["s1"].Helpful, sessions["s1"].Unhelpful));
        Assert.Equal(new[] { "1.1.1.1", "1.2.1.1" }, sessions["s2"].AnsweredIds);
    }

    [Fact]
    public void Report_ShowsPopularRareNeverAskedSectionsAndUnanswered()
    {
        Fill();
        var report = Analytics.Report(DbPath, Catalog, Analytics.Period.All, top: 1);

        Assert.Contains("Бесед: 2", report);
        Assert.Contains("выдано ответов: 4 (сразу: 1, после выбора из меню: 3)", report);
        Assert.Contains("Самые популярные вопросы (топ 1):", report);
        Assert.Contains("1. 1.2.1.1   ответов: 3", report);
        Assert.Contains("Наименее популярные вопросы", report);
        Assert.Contains("1. 1.1.1.1   ответов: 1", report);
        Assert.Contains("Ни разу не выдавались (2 из 4)", report);
        Assert.Contains("1.3.3.2   Забыл пароль от кабинета", report);
        Assert.Contains("1.2.1   ответов: 3    (75%)", report);
        Assert.Contains("Счёт", report);
        Assert.Contains("«какую лицензию брать»", report); // запрос с отзывом «не помогло»
        Assert.Contains("повторов: 2    «абракадабра»", report); // регистр и пробелы не важны
    }

    [Fact]
    public void Period_FiltersByDate()
    {
        Fill();
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Contains("Бесед: 2", Analytics.Report(DbPath, Catalog, new Analytics.Period(today, today)));
        Assert.Contains("Бесед пока нет", Analytics.Report(DbPath, Catalog, new Analytics.Period(today.AddDays(1), null)));
        Assert.Contains("Бесед пока нет", Analytics.Report(DbPath, Catalog, new Analytics.Period(null, today.AddDays(-1))));
    }

    [Fact]
    public void SessionList_AndSessionReport()
    {
        Fill();
        var list = Analytics.SessionList(DbPath, Analytics.Period.All);
        Assert.Contains("s1", list);
        Assert.Contains("вопросов: 3", list);

        var one = Analytics.SessionReport(DbPath, "s2", Catalog);
        Assert.Contains("Компьютер: pc2", one);
        Assert.Contains("ответов: 2 (сразу: 0, после выбора из меню: 2)", one);
        Assert.Contains("1.1.1.1   Какую лицензию выбрать?", one);
        Assert.Contains("Пользователь: какую лицензию брать", one);
        Assert.Contains("не найдена", Analytics.SessionReport(DbPath, "нет-такой", Catalog));
    }

    [Fact]
    public void ExportCsv_WritesExcelFriendlyTables()
    {
        Fill();
        var files = Analytics.ExportCsv(DbPath, Catalog, Analytics.Period.All, _tmp.Sub("csv"));

        Assert.Equal(new[] { "questions.csv", "sessions.csv", "unanswered.csv", "days.csv" }, files.Select(Path.GetFileName));
        var bytes = File.ReadAllBytes(_tmp.Sub("csv", "questions.csv"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM — Excel поймёт кодировку
        var lines = File.ReadAllLines(_tmp.Sub("csv", "questions.csv"), Encoding.UTF8);
        Assert.StartsWith("Номер;Вопрос;Раздел", lines[0]);
        Assert.StartsWith("1.2.1.1;Как получить счёт?;1.2.1;Счёт;3;1;2;1;0;", lines[1]);
        Assert.Equal(5, lines.Length); // заголовок + 4 вопроса, в том числе не выдававшиеся
        Assert.Contains("абракадабра;2;", File.ReadAllText(_tmp.Sub("csv", "unanswered.csv")));
        Assert.Contains("s2;", File.ReadAllText(_tmp.Sub("csv", "sessions.csv")));
    }

    [Fact]
    public void Report_WithoutDatabase() =>
        Assert.Contains("ещё не создана", Analytics.Report(DbPath, Catalog, Analytics.Period.All));

    [Fact]
    public void Conversation_WritesSummaryToSessionLog()
    {
        var engine = ChatEngine.Create(TestSupport.FindContentDir(), "stemmer", semantic: false);
        engine.Config.Sessions.Path = _tmp.Sub("sessions");
        engine.Config.Database.Path = _tmp.Sub("cryptochat.db");
        engine.Config.Email.Enabled = false;

        var conversation = new Conversation(engine);
        foreach (var line in new[] { "Можно оплатить счёт на ИП банковской картой?", "да", "абракадабра", "выход" })
        {
            conversation.User(line);
            conversation.Bot(line, engine.Ask(line));
        }
        conversation.Close();

        var text = File.ReadAllText(_tmp.Sub("sessions", conversation.SessionId + ".txt"));
        Assert.Contains("Итог беседы:", text);
        Assert.Contains("вопросов: 2, ответов: 1 (сразу: 1, после выбора из меню: 0), не ответил сразу: 1", text);
        Assert.Contains("отзывы: помогло 1, не помогло 0", text);
        Assert.Contains("1.2.2.4", text);
        Assert.True(text.IndexOf("Итог беседы:", StringComparison.Ordinal) < text.IndexOf(SessionRecorder.EndMarker, StringComparison.Ordinal));

        // В базе бесед — тот же итог.
        var stat = Analytics.Sessions(Analytics.Load(_tmp.Sub("cryptochat.db"), Analytics.Period.All)).Single();
        Assert.Equal((2, 1, 1, 1), (stat.Questions, stat.Answers, stat.Unanswered, stat.Helpful));
    }
}

public class StatsOptionsTests
{
    [Fact]
    public void Parse_PeriodTopCsvAndSessions()
    {
        var o = Options.Parse(new[] { "--stats", "--from", "29.10.2026", "--to", "2026-10-31", "--top", "20" });
        Assert.Null(o.Error);
        Assert.Equal((new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 31), 20), (o.From!.Value, o.To!.Value, o.Top));

        var csv = Options.Parse(new[] { "--stats-csv", "--from", "2026-10-01" });
        Assert.True(csv.StatsCsv);
        Assert.Null(csv.StatsCsvFolder); // «--from» — следующий ключ, а не папка
        Assert.Equal("out", Options.Parse(new[] { "--stats-csv", "out" }).StatsCsvFolder);
        Assert.Equal("s1", Options.Parse(new[] { "--session", "s1" }).Session);
    }

    [Theory]
    [InlineData("--from", "32.13.2026", "дата")]
    [InlineData("--top", "0", "число")]
    [InlineData("--session", null, "номер сессии")]
    public void Parse_ReportsBadValues(string key, string? value, string message)
    {
        var o = Options.Parse(value == null ? new[] { key } : new[] { key, value });
        Assert.Contains(message, o.Error);
    }
}
