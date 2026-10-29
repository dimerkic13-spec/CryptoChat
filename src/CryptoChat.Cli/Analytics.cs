using System.Globalization;
using System.Text;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoChat.Cli;

/// <summary>
/// Анализ базы бесед (logs/cryptochat.db): какие вопросы задают чаще и реже, как бот на них отвечает,
/// что осталось без ответа, статистика по сессиям, разделам и дням. Схема базы не меняется — база
/// по-прежнему общая с Python-версией.
/// </summary>
internal static class Analytics
{
    /// <summary>Вопрос базы знаний: номер, заголовок и раздел (блок), к которому он относится.</summary>
    public sealed record CatalogQuestion(string Id, string Title, string Section);

    /// <summary>Что известно о базе знаний: вопросы и названия разделов (номер → название).</summary>
    public sealed record Catalog(IReadOnlyList<CatalogQuestion> Questions, IReadOnlyDictionary<string, string> Sections)
    {
        public static Catalog Empty { get; } = new(Array.Empty<CatalogQuestion>(), new Dictionary<string, string>());

        public static Catalog FromTitles(IReadOnlyDictionary<string, string> titles) =>
            new(titles.Select(t => new CatalogQuestion(t.Key, t.Value, SectionOf(t.Key))).ToList(), new Dictionary<string, string>());

        /// <summary>Каталог из content/; если база знаний с ошибкой — пустой (статистика нужна и тогда).</summary>
        public static Catalog Load(string contentDir)
        {
            try
            {
                var content = KnowledgeLoader.LoadContent(contentDir);
                var sections = new Dictionary<string, string>();
                void Walk(IEnumerable<RawCategory> nodes)
                {
                    foreach (var node in nodes)
                    {
                        sections.TryAdd(node.Id, node.Name);
                        Walk(node.Children);
                    }
                }
                Walk(content.Taxonomy.Categories);
                var questions = content.Questions
                    .Where(q => !string.Equals(q.Status, "draft", StringComparison.OrdinalIgnoreCase))
                    .Select(q => new CatalogQuestion(q.Id, q.Title, string.IsNullOrEmpty(q.Category) ? SectionOf(q.Id) : q.Category))
                    .DistinctBy(q => q.Id)
                    .ToList();
                return new Catalog(questions, sections);
            }
            catch (ContentException)
            {
                return Empty;
            }
        }
    }

    /// <summary>Период отчёта: даты включительно; null — без ограничения.</summary>
    public sealed record Period(DateOnly? From, DateOnly? To)
    {
        public static Period All { get; } = new(null, null);

        public bool Contains(string? time)
        {
            if (string.IsNullOrEmpty(time) || time.Length < 10)
                return From == null && To == null;
            var date = time[..10];
            return (From == null || string.CompareOrdinal(date, From.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0)
                && (To == null || string.CompareOrdinal(date, To.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) <= 0);
        }

        public override string ToString() => (From, To) switch
        {
            (null, null) => "всё время",
            (_, null) => $"с {From:dd.MM.yyyy}",
            (null, _) => $"по {To:dd.MM.yyyy}",
            _ => $"с {From:dd.MM.yyyy} по {To:dd.MM.yyyy}"
        };
    }

    /// <summary>Как часто и как бот отвечал на вопрос базы знаний.</summary>
    public sealed record QuestionStat(string Id, string Title, string Section, int Answers, int Direct, int ViaMenu,
        int Helpful, int Unhelpful, string? LastTime);

    /// <summary>Итог одной сессии.</summary>
    public sealed record SessionStat(string Id, string Started, string? Finished, string Computer, string Version,
        int Questions, int Answers, int ViaMenu, int Unanswered, int Helpful, int Unhelpful, IReadOnlyList<string> AnsweredIds);

    public sealed record Message(long Id, string SessionId, string Time, string Role, string? Kind, string Text, string? QuestionId);

    public sealed record Feedback(string? SessionId, string Time, string? Query, string QuestionId, bool Helpful);

    public sealed record Unanswered(string? SessionId, string Time, string Query, string Kind);

    public sealed record Session(string Id, string Started, string? Finished, string Computer, string Version);

    /// <summary>Строки базы бесед за период.</summary>
    public sealed class Data
    {
        public List<Session> Sessions { get; } = new();
        public List<Message> Messages { get; } = new();
        public List<Feedback> Feedbacks { get; } = new();
        public List<Unanswered> Unanswered { get; } = new();
    }

    // ---------- Чтение базы ----------

    public static Data Load(string path, Period period)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString());
        connection.Open();

        IEnumerable<object?[]> Rows(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < row.Length; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                yield return row;
            }
        }

        string S(object? value) => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        var data = new Data();
        foreach (var r in Rows("SELECT id, started, finished, computer, version FROM sessions ORDER BY started, id"))
            if (period.Contains(S(r[1])))
                data.Sessions.Add(new Session(S(r[0]), S(r[1]), r[2] as string, S(r[3]), S(r[4])));
        foreach (var r in Rows("SELECT id, session_id, time, role, kind, text, question_id FROM messages ORDER BY id"))
            if (period.Contains(S(r[2])))
                data.Messages.Add(new Message(Convert.ToInt64(r[0]), S(r[1]), S(r[2]), S(r[3]), r[4] as string, S(r[5]), r[6] as string));
        foreach (var r in Rows("SELECT session_id, time, query, question_id, helpful FROM feedback ORDER BY id"))
            if (period.Contains(S(r[1])))
                data.Feedbacks.Add(new Feedback(r[0] as string, S(r[1]), r[2] as string, S(r[3]), Convert.ToInt64(r[4]) != 0));
        foreach (var r in Rows("SELECT session_id, time, query, kind FROM unanswered ORDER BY id"))
            if (period.Contains(S(r[1])))
                data.Unanswered.Add(new Unanswered(r[0] as string, S(r[1]), S(r[2]), S(r[3])));
        return data;
    }

    // ---------- Анализ ----------

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsDigit);

    private static string SectionOf(string questionId)
    {
        var dot = questionId.LastIndexOf('.');
        return dot > 0 ? questionId[..dot] : questionId;
    }

    /// <summary>Ответ на вопрос из базы: сразу (после текста) или после выбора пункта меню (после номера).</summary>
    private sealed record AnswerEvent(string SessionId, string Time, string QuestionId, bool ViaMenu);

    private static List<AnswerEvent> Answers(Data data)
    {
        var result = new List<AnswerEvent>();
        var lastUser = new Dictionary<string, string>();
        foreach (var m in data.Messages)
        {
            if (m.Role == "user")
                lastUser[m.SessionId] = m.Text.Trim();
            else if (m.Kind == nameof(ReplyKind.Answer) && m.QuestionId != null)
                result.Add(new AnswerEvent(m.SessionId, m.Time, m.QuestionId, IsDigits(lastUser.GetValueOrDefault(m.SessionId, ""))));
        }
        return result;
    }

    /// <summary>Сколько вопросов задал пользователь: сообщения, кроме номеров пунктов меню, команд и «да»/«нет» на отзыв.</summary>
    private static Dictionary<string, int> QuestionsBySession(Data data)
    {
        var result = new Dictionary<string, int>();
        Message? pending = null;
        foreach (var m in data.Messages)
        {
            if (m.Role == "user")
            {
                pending = m;
                continue;
            }
            if (pending != null && pending.SessionId == m.SessionId)
            {
                var text = pending.Text.Trim();
                if (!IsDigits(text) && !text.StartsWith('/') && m.Kind != nameof(ReplyKind.Feedback) && m.Kind != nameof(ReplyKind.Command))
                    result[m.SessionId] = result.GetValueOrDefault(m.SessionId) + 1;
            }
            pending = null;
        }
        return result;
    }

    /// <summary>Статистика по всем вопросам базы знаний (и по тем, что есть только в базе бесед).</summary>
    public static List<QuestionStat> Questions(Data data, Catalog catalog)
    {
        var answers = Answers(data);
        var byId = answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.ToList());
        var votes = data.Feedbacks.GroupBy(f => f.QuestionId).ToDictionary(g => g.Key, g => g.ToList());
        var known = catalog.Questions.ToDictionary(q => q.Id);
        var ids = catalog.Questions.Select(q => q.Id).Concat(byId.Keys).Concat(votes.Keys).Distinct();

        return ids.Select(id =>
        {
            var list = byId.GetValueOrDefault(id) ?? new List<AnswerEvent>();
            var fb = votes.GetValueOrDefault(id) ?? new List<Feedback>();
            var q = known.GetValueOrDefault(id);
            return new QuestionStat(id, q?.Title ?? "(нет в базе знаний)", q?.Section ?? SectionOf(id), list.Count,
                list.Count(a => !a.ViaMenu), list.Count(a => a.ViaMenu), fb.Count(f => f.Helpful), fb.Count(f => !f.Helpful),
                list.Count > 0 ? list.Max(a => a.Time) : null);
        }).ToList();
    }

    public static List<SessionStat> Sessions(Data data)
    {
        var answers = Answers(data).GroupBy(a => a.SessionId).ToDictionary(g => g.Key, g => g.ToList());
        var questions = QuestionsBySession(data);
        var unanswered = data.Unanswered.Where(u => u.SessionId != null).GroupBy(u => u.SessionId!).ToDictionary(g => g.Key, g => g.Count());
        var votes = data.Feedbacks.Where(f => f.SessionId != null).GroupBy(f => f.SessionId!).ToDictionary(g => g.Key, g => g.ToList());
        return data.Sessions.Select(s =>
        {
            var a = answers.GetValueOrDefault(s.Id) ?? new List<AnswerEvent>();
            var fb = votes.GetValueOrDefault(s.Id) ?? new List<Feedback>();
            return new SessionStat(s.Id, s.Started, s.Finished, s.Computer, s.Version, questions.GetValueOrDefault(s.Id),
                a.Count, a.Count(x => x.ViaMenu), unanswered.GetValueOrDefault(s.Id), fb.Count(f => f.Helpful), fb.Count(f => !f.Helpful),
                a.Select(x => x.QuestionId).ToList());
        }).ToList();
    }

    /// <summary>Запросы без ответа, сложенные без учёта регистра и лишних пробелов: (запрос, повторов, последний раз).</summary>
    public static List<(string Query, int Count, string Last)> UnansweredGroups(Data data)
    {
        var grouped = new Dictionary<string, (int Count, string Last)>();
        foreach (var u in data.Unanswered)
        {
            var key = string.Join(" ", u.Query.ToLowerInvariant().Replace('ё', 'е')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (key.Length == 0)
                continue;
            var item = grouped.GetValueOrDefault(key, (Count: 0, Last: ""));
            grouped[key] = (item.Count + 1, string.CompareOrdinal(u.Time, item.Last) > 0 ? u.Time : item.Last);
        }
        // Сначала частые, при равенстве — более свежие.
        return grouped
            .OrderByDescending(x => x.Value.Count)
            .ThenByDescending(x => x.Value.Last, StringComparer.Ordinal)
            .Select(x => (x.Key, x.Value.Count, x.Value.Last))
            .ToList();
    }

    /// <summary>По дням: (дата, бесед, вопросов, ответов, без ответа).</summary>
    public static List<(string Date, int Sessions, int Questions, int Answers, int Unanswered)> Days(Data data)
    {
        var sessions = Sessions(data);
        return sessions
            .GroupBy(s => s.Started.Length >= 10 ? s.Started[..10] : s.Started)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count(), g.Sum(s => s.Questions), g.Sum(s => s.Answers), g.Sum(s => s.Unanswered)))
            .ToList();
    }

    // ---------- Отчёт ----------

    private static string Short(string text, int max = 60) => text.Length > max ? text[..max] + "…" : text;

    private static string Percent(int part, int whole) => whole == 0 ? "—" : $"{100.0 * part / whole:F0}%";

    private static string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} ч {span.Minutes} мин" : $"{span.Minutes} мин {span.Seconds} с";

    private static TimeSpan? Length(string started, string? finished) =>
        finished != null
        && DateTime.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
        && DateTime.TryParse(finished, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b) && b >= a
            ? b - a
            : null;

    /// <summary>Текстовый отчёт для --stats.</summary>
    public static string Report(string path, Catalog catalog, Period period, int top = 10)
    {
        if (!File.Exists(path))
            return $"База бесед ещё не создана: {path}\nОна появится после первой беседы (config.json → database.enabled).";

        var data = Load(path, period);
        var lines = new List<string> { $"База бесед: {path}", $"Период: {period}" };
        if (data.Sessions.Count == 0 && data.Messages.Count == 0)
        {
            lines.Add("Бесед пока нет.");
            return string.Join("\n", lines);
        }

        var sessions = Sessions(data);
        var questions = Questions(data, catalog);
        var asked = sessions.Sum(s => s.Questions);
        var answers = questions.Sum(q => q.Answers);
        var viaMenu = questions.Sum(q => q.ViaMenu);
        var helpful = data.Feedbacks.Count(f => f.Helpful);
        var unhelpful = data.Feedbacks.Count - helpful;
        var lengths = sessions.Select(s => Length(s.Started, s.Finished)).OfType<TimeSpan>().ToList();

        if (data.Sessions.Count > 0)
        {
            var first = data.Sessions.Min(s => s.Started)!;
            var last = data.Sessions.Max(s => s.Started)!;
            lines.Add($"Бесед: {data.Sessions.Count} (с {first[..Math.Min(10, first.Length)]} по {last[..Math.Min(10, last.Length)]})");
        }
        lines.Add($"Вопросов от пользователей: {asked}, выдано ответов: {answers} (сразу: {answers - viaMenu}, после выбора из меню: {viaMenu}), " +
                  $"не ответил сразу: {data.Unanswered.Count}");
        lines.Add($"Отзывы: помогло {helpful}, не помогло {unhelpful} (доля «помогло»: {Percent(helpful, helpful + unhelpful)})");
        if (sessions.Count > 0)
        {
            var average = lengths.Count > 0 ? $", средняя длительность: {Duration(TimeSpan.FromSeconds(lengths.Average(l => l.TotalSeconds)))}" : "";
            lines.Add($"В среднем за беседу: вопросов {1.0 * asked / sessions.Count:F1}, ответов {1.0 * answers / sessions.Count:F1}{average}");
        }

        string Row(int rank, QuestionStat q) =>
            $"  {rank,2}. {q.Id,-9} ответов: {q.Answers,-4} (сразу {q.Direct}, из меню {q.ViaMenu})  " +
            $"помогло: {q.Helpful,-3} не помогло: {q.Unhelpful,-3} {Short(q.Title)}";

        var popular = questions.Where(q => q.Answers > 0)
            .OrderByDescending(q => q.Answers).ThenBy(q => q.Id, StringComparer.Ordinal).Take(top).ToList();
        if (popular.Count > 0)
        {
            lines.Add($"\nСамые популярные вопросы (топ {top}):");
            lines.AddRange(popular.Select((q, i) => Row(i + 1, q)));
        }

        var rare = questions.Where(q => q.Answers > 0 && !popular.Contains(q))
            .OrderBy(q => q.Answers).ThenBy(q => q.Id, StringComparer.Ordinal).Take(top).ToList();
        if (rare.Count > 0)
        {
            lines.Add($"\nНаименее популярные вопросы (задавали, но редко, {rare.Count}):");
            lines.AddRange(rare.Select((q, i) => Row(i + 1, q)));
        }

        var never = questions.Where(q => q.Answers == 0 && catalog.Questions.Any(c => c.Id == q.Id))
            .OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
        if (never.Count > 0)
        {
            lines.Add($"\nНи разу не выдавались ({never.Count} из {catalog.Questions.Count}) — возможно, их ищут другими словами:");
            lines.AddRange(never.Select(q => $"  {q.Id,-9} {Short(q.Title)}"));
        }

        var sections = questions.Where(q => q.Answers > 0)
            .GroupBy(q => q.Section)
            .Select(g => (Section: g.Key, Answers: g.Sum(q => q.Answers), Helpful: g.Sum(q => q.Helpful), Unhelpful: g.Sum(q => q.Unhelpful)))
            .OrderByDescending(x => x.Answers).ThenBy(x => x.Section, StringComparer.Ordinal).ToList();
        if (sections.Count > 0)
        {
            lines.Add("\nПо разделам:");
            foreach (var s in sections)
                lines.Add($"  {s.Section,-7} ответов: {s.Answers,-4} ({Percent(s.Answers, answers)})  помогло: {s.Helpful,-3} не помогло: {s.Unhelpful,-3} " +
                          catalog.Sections.GetValueOrDefault(s.Section, ""));
        }

        var bad = questions.Where(q => q.Unhelpful > 0)
            .OrderByDescending(q => q.Unhelpful).ThenBy(q => q.Id, StringComparer.Ordinal).Take(top).ToList();
        if (bad.Count > 0)
        {
            lines.Add("\nОтветы с отзывом «не помогло» — стоит проверить текст и ключевые слова:");
            foreach (var q in bad)
            {
                var examples = string.Join("; ", data.Feedbacks
                    .Where(f => !f.Helpful && f.QuestionId == q.Id && !string.IsNullOrEmpty(f.Query))
                    .OrderByDescending(f => f.Time, StringComparer.Ordinal).Take(3)
                    .Select(f => $"«{f.Query}»"));
                lines.Add($"  {q.Id,-10} не помогло: {q.Unhelpful,-4} {Short(q.Title)}");
                if (examples.Length > 0)
                    lines.Add($"             запросы: {examples}");
            }
        }

        var missing = UnansweredGroups(data).Take(top).ToList();
        if (missing.Count > 0)
        {
            lines.Add("\nЗапросы, на которые бот не ответил сразу — кандидаты в новые вопросы или ключи:");
            foreach (var (query, count, lastTime) in missing)
                lines.Add($"  повторов: {count,-4} «{query}»  (последний: {lastTime[..Math.Min(16, lastTime.Length)]})");
        }

        var days = Days(data);
        if (days.Count > 1)
        {
            lines.Add("\nПо дням (последние 14):");
            foreach (var d in days.TakeLast(14))
                lines.Add($"  {d.Date}  бесед: {d.Sessions,-3} вопросов: {d.Questions,-4} ответов: {d.Answers,-4} без ответа: {d.Unanswered}");
        }

        return string.Join("\n", lines);
    }

    // ---------- Сессии ----------

    /// <summary>Список последних сессий для --sessions.</summary>
    public static string SessionList(string path, Period period, int count = 20)
    {
        if (!File.Exists(path))
            return $"База бесед ещё не создана: {path}";
        var sessions = Sessions(Load(path, period));
        if (sessions.Count == 0)
            return $"Бесед нет (период: {period}).";
        var lines = new List<string> { $"Последние беседы ({Math.Min(count, sessions.Count)} из {sessions.Count}, период: {period}):" };
        foreach (var s in sessions.TakeLast(count).Reverse())
            lines.Add($"  {s.Id}  {s.Started[..Math.Min(16, s.Started.Length)]}  вопросов: {s.Questions,-3} ответов: {s.Answers,-3} " +
                      $"без ответа: {s.Unanswered,-3} отзывы: +{s.Helpful}/-{s.Unhelpful}  {s.Computer}");
        lines.Add("Подробно о беседе: --session <номер>");
        return string.Join("\n", lines);
    }

    /// <summary>Итог и переписка одной сессии для --session.</summary>
    public static string SessionReport(string path, string sessionId, Catalog catalog)
    {
        if (!File.Exists(path))
            return $"База бесед ещё не создана: {path}";
        var data = Load(path, Period.All);
        var stat = Sessions(data).FirstOrDefault(s => s.Id == sessionId);
        if (stat == null)
            return $"Беседа {sessionId} не найдена. Список бесед: --sessions";
        var titles = catalog.Questions.ToDictionary(q => q.Id, q => q.Title);
        var lines = new List<string> { $"Беседа {stat.Id}", $"Начало: {stat.Started}, конец: {stat.Finished ?? "не завершена"}" };
        if (Length(stat.Started, stat.Finished) is { } length)
            lines[^1] += $", длительность: {Duration(length)}";
        lines.Add($"Компьютер: {stat.Computer}, версия: {stat.Version}");
        lines.AddRange(SummaryLines(stat.Questions, stat.Answers, stat.ViaMenu, stat.Unanswered, stat.Helpful, stat.Unhelpful,
            stat.AnsweredIds.Select(id => (id, titles.GetValueOrDefault(id, "")))));
        lines.Add("\nПереписка:");
        foreach (var m in data.Messages.Where(m => m.SessionId == sessionId))
        {
            var who = m.Role == "user" ? "Пользователь" : $"Бот ({m.Kind})";
            var text = m.Text.Replace("\n", "\n      ");
            lines.Add($"  [{(m.Time.Length >= 19 ? m.Time[11..19] : m.Time)}] {who}: {text}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Строки итога беседы — одинаковые в журнале беседы и в --session.</summary>
    public static List<string> SummaryLines(int questions, int answers, int viaMenu, int unanswered, int helpful, int unhelpful,
        IEnumerable<(string Id, string Title)> answered)
    {
        var lines = new List<string>
        {
            "Итог беседы:",
            $"  вопросов: {questions}, ответов: {answers} (сразу: {answers - viaMenu}, после выбора из меню: {viaMenu}), не ответил сразу: {unanswered}",
            $"  отзывы: помогло {helpful}, не помогло {unhelpful}"
        };
        var list = answered.ToList();
        if (list.Count > 0)
        {
            lines.Add("  выданные ответы:");
            lines.AddRange(list.Select(a => $"    {a.Id,-9} {Short(a.Title, 70)}"));
        }
        return lines;
    }

    // ---------- Выгрузка в CSV ----------

    /// <summary>
    /// Таблицы для Excel: questions.csv, sessions.csv, unanswered.csv, days.csv.
    /// Разделитель «;» и UTF-8 с BOM — так Excel в русской Windows открывает файл двойным щелчком.
    /// </summary>
    public static IReadOnlyList<string> ExportCsv(string path, Catalog catalog, Period period, string folder)
    {
        var data = Load(path, period);
        Directory.CreateDirectory(folder);
        var written = new List<string>();

        void Write(string name, string[] header, IEnumerable<object?[]> rows)
        {
            static string Cell(object? value)
            {
                var text = value switch
                {
                    null => "",
                    double d => d.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")),
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
                };
                return text.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
            }
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", header));
            foreach (var row in rows)
                sb.AppendLine(string.Join(";", row.Select(Cell)));
            var file = Path.Combine(folder, name);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            written.Add(file);
        }

        var questions = Questions(data, catalog)
            .OrderByDescending(q => q.Answers).ThenBy(q => q.Id, StringComparer.Ordinal).ToList();
        Write("questions.csv",
            new[] { "Номер", "Вопрос", "Раздел", "Название раздела", "Ответов", "Сразу", "Из меню", "Помогло", "Не помогло", "Последний раз" },
            questions.Select(q => new object?[] { q.Id, q.Title, q.Section, catalog.Sections.GetValueOrDefault(q.Section, ""),
                q.Answers, q.Direct, q.ViaMenu, q.Helpful, q.Unhelpful, q.LastTime }));

        Write("sessions.csv",
            new[] { "Сессия", "Начало", "Конец", "Компьютер", "Версия", "Вопросов", "Ответов", "Из меню", "Без ответа", "Помогло", "Не помогло", "Ответы" },
            Sessions(data).Select(s => new object?[] { s.Id, s.Started, s.Finished, s.Computer, s.Version, s.Questions, s.Answers,
                s.ViaMenu, s.Unanswered, s.Helpful, s.Unhelpful, string.Join(", ", s.AnsweredIds) }));

        Write("unanswered.csv", new[] { "Запрос", "Повторов", "Последний раз" },
            UnansweredGroups(data).Select(u => new object?[] { u.Query, u.Count, u.Last }));

        Write("days.csv", new[] { "Дата", "Бесед", "Вопросов", "Ответов", "Без ответа" },
            Days(data).Select(d => new object?[] { d.Date, d.Sessions, d.Questions, d.Answers, d.Unanswered }));

        return written;
    }
}
