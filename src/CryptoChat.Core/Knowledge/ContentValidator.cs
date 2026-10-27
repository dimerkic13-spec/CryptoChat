using System.Globalization;
using CryptoChat.Core.Text;

namespace CryptoChat.Core.Knowledge;

/// <summary>
/// Проверяет данные до того, как по ним строится база. Ошибки (V001–V012) мешают запуску,
/// предупреждения (V013, V014, V016) только сообщают о подозрительных местах.
/// </summary>
public static class ContentValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ContentSet content, IEnumerable<string>? stopWords = null)
    {
        var issues = new List<ValidationIssue>();
        var stop = new HashSet<string>((stopWords ?? TextProcessor.DefaultStopWords).Select(Normalizer.Normalize));

        void Err(string code, string where, string message) =>
            issues.Add(new ValidationIssue(code, Severity.Error, where, message));
        void Warn(string code, string where, string message) =>
            issues.Add(new ValidationIssue(code, Severity.Warning, where, message));

        void CheckKeywords(string where, List<RawKeyword> keywords, bool isQuestion)
        {
            if (isQuestion && keywords.Count < 3)
                 Err("V006", where, $"Ключевых слов должно быть не меньше 3, сейчас {keywords.Count}.");

            var seenStems = new HashSet<string>();
            foreach (var k in keywords)
            {
                var word = Normalizer.Normalize((k.Word ?? "").Trim());

                if (k.Weight < 0.1 || k.Weight > 10)
                    Err("V007", where, $"Вес ключа «{k.Word}» вне диапазона 0.1–10: {k.Weight}.");

                if (word.Length == 0 || word.Any(char.IsWhiteSpace))
                {
                    Err("V010", where, $"Ключ «{k.Word}» пуст или состоит из нескольких слов.");
                    continue;
                }

                if (word.Any(IsLatin) && word.Any(IsCyrillic))
                    Err("V009", where, $"Ключ «{k.Word}» смешивает латиницу и кириллицу.");

                var stem = Stemmer.Stem(word);
                if (stem.Length == 0)
                {
                    Err("V010", where, $"Ключ «{k.Word}» пуст после стемминга.");
                    continue;
                }

                if (!seenStems.Add(stem))
                    Err("V008", where, $"Ключ «{k.Word}» повторяется (основа «{stem}»).");

                if (stop.Contains(word))
                    Warn("V014", where, $"Ключ «{k.Word}» совпадает со стоп-словом и никогда не сработает.");
            }
        }

        // ---------- Категории ----------
        var categories = new Dictionary<string, RawCategory>();

        void Walk(RawCategory category, RawCategory? parent)
        {
            var where = $"категория {category.Id}";

            if (string.IsNullOrWhiteSpace(category.Id))
                Err("V001", "категория", "У категории пустой id.");
            else if (!categories.TryAdd(category.Id, category))
                Err("V001", where, "Id категории повторяется.");

            if (parent != null && !(category.Id ?? "").StartsWith(parent.Id + ".", StringComparison.Ordinal))
                Err("V011", where, $"Id дочерней категории должен начинаться с «{parent.Id}.».");

            CheckKeywords(where, category.Keywords, false);

            foreach (var child in category.Children)
                Walk(child, category);
        }

        foreach (var root in content.Taxonomy.Categories)
            Walk(root, null);

        // ---------- Вопросы ----------
        var questionIds = new HashSet<string>();
        var categoriesWithQuestions = new HashSet<string>();

        foreach (var q in content.Questions)
        {
            var where = $"вопрос {q.Id}";

            if (string.IsNullOrWhiteSpace(q.Id))
                Err("V001", "вопрос", "У вопроса пустой id.");
            else if (!questionIds.Add(q.Id))
                Err("V001", where, "Id вопроса повторяется.");

            if (!categories.TryGetValue(q.Category ?? "", out var category))
            {
                Err("V002", where, $"Категория «{q.Category}» не найдена в taxonomy.json.");
            }
            else
            {
                if (category.Children.Count > 0)
                    Err("V002", where, $"Категория «{q.Category}» не конечная: вопрос можно привязать только к категории без дочерних.");
                else
                    categoriesWithQuestions.Add(category.Id);
            }

            if (!(q.Id ?? "").StartsWith((q.Category ?? "") + ".", StringComparison.Ordinal))
                Err("V003", where, $"Id вопроса должен начинаться с «{q.Category}.».");

            if (string.IsNullOrWhiteSpace(q.Title))
                Err("V004", where, "Пустой заголовок (title).");
            if (string.IsNullOrWhiteSpace(q.Answer))
                Err("V004", where, "Пустой ответ (answer).");

            if (q.Source == null || string.IsNullOrWhiteSpace(q.Source.Url)
                || !q.Source.Url.StartsWith("https://", StringComparison.Ordinal))
                Err("V005", where, "Нужна ссылка на источник source.url, начинающаяся с https://.");

            CheckKeywords(where, q.Keywords, true);

            var revision = q.Source?.Revision;
            if (revision != null && !DateOnly.TryParseExact(revision, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                Warn("V016", where, $"Дата редакции «{revision}» не в формате ГГГГ-ММ-ДД.");
        }

        // ---------- Синонимы ----------
        var seenSynonyms = new HashSet<string>();
        for (int i = 0; i < content.Synonyms.Groups.Count; i++)
        {
            var group = content.Synonyms.Groups[i];
            var where = $"синонимы, группа {i + 1}";

            if (group.Count < 2)
            {
                Err("V012", where, "В группе должно быть минимум два слова.");
                continue;
            }

            if (Normalizer.Normalize(group[0]).Trim().Contains(' '))
                Err("V012", where, "Первое (каноническое) слово группы должно быть одним словом.");

            foreach (var member in group)
            {
                var normalized = Normalizer.Normalize(member).Trim();
                if (normalized.Length == 0) continue;
                var key = normalized.Contains(' ') ? normalized : Stemmer.Stem(normalized);
                if (!seenSynonyms.Add(key))
                    Err("V012", where, $"Слово «{member}» повторяется или входит в несколько групп.");
            }
        }

        // ---------- Пустые разделы ----------
        foreach (var category in categories.Values)
        {
            if (category.Children.Count == 0 && !categoriesWithQuestions.Contains(category.Id))
                Warn("V013", $"категория {category.Id}", $"В разделе «{category.Name}» нет ни одного вопроса.");
        }

        return issues;
    }

    private static bool IsLatin(char c) => c is >= 'a' and <= 'z';
    private static bool IsCyrillic(char c) => c is >= 'а' and <= 'я' or 'ё';
}
