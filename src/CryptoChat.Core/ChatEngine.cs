using System.Globalization;
using System.Text;
using CryptoChat.Core.Answer;
using CryptoChat.Core.Dialog;
using CryptoChat.Core.Knowledge;
using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;
using CryptoChat.Core.Text;

namespace CryptoChat.Core;

/// <summary>Единственная точка входа: Ask(текст) → ChatReply. Консоль и будущие интерфейсы только вызывают её.</summary>
public sealed class ChatEngine
{
    public static readonly IReadOnlySet<string> ExitWords =
        new HashSet<string> { "выход", "exit", "quit", "/выход", "/exit", "/quit" };

    private readonly KnowledgeBase _kb;
    private readonly AppConfig _config;
    private readonly ITextProcessor _text;
    private readonly IRetriever _retriever;
    private readonly IDialogPolicy _policy;
    private readonly IAnswerComposer _composer;
    private readonly ChatSession _session = new();

    public bool DebugEnabled { get; set; }

    /// <summary>Вызывается, когда пользователь оценил ответ словом «да» или «нет».</summary>
    public event Action<FeedbackEntry>? FeedbackGiven;

    /// <summary>Учёт опечаток для поиска вопросов и для меню уточнения по веткам (null — выключен).</summary>
    public FuzzyMatcher? Fuzzy { get; }

    /// <summary>Чем приводятся слова к основе: «стеммер» или «pymorphy3».</summary>
    public string Morphology => _kb.Lemmatizer.Name;

    /// <summary>Почему выбран такой способ (например, «словари морфологии не найдены»).</summary>
    public string? MorphologyNote { get; set; }

    /// <summary>Номер сессии задаёт консоль (или другой интерфейс); показывается по команде /сессия.</summary>
    public string? SessionId { get; set; }

    private bool Guided => _config.Dialog.Guided;
    public KnowledgeBase KnowledgeBase => _kb;
    public AppConfig Config => _config;
    public ITextProcessor Text => _text;

    public ChatEngine(
        KnowledgeBase kb,
        AppConfig config,
        ITextProcessor? text = null,
        IRetriever? retriever = null,
        IDialogPolicy? policy = null,
        IAnswerComposer? composer = null)
    {
        _kb = kb;
        _config = config;
        _text = text ?? new TextProcessor(synonyms: kb.Synonyms, lemmatizer: kb.Lemmatizer);
        // Один и тот же учёт опечаток для поиска вопросов и для меню уточнения по веткам.
        Fuzzy = retriever is KeywordRetriever keywordRetriever
            ? keywordRetriever.Fuzzy
            : retriever == null ? FuzzyMatcher.FromConfig(config.Fuzzy) : null;
        _retriever = retriever ?? new KeywordRetriever(kb, config, Fuzzy);
        _policy = policy ?? new DialogPolicy(config, kb);
        _composer = composer ?? new TemplateComposer();
    }

    /// <summary>
    /// Загружает и проверяет каталог content/ и создаёт готовый движок.
    /// morphology переопределяет text.morphology из config.json (нужно для сравнения режимов).
    /// </summary>
    public static ChatEngine Create(string contentDirectory, string? morphology = null)
    {
        var config = AppConfig.Load(contentDirectory);
        var (lemmatizer, note) = CreateLemmatizer(morphology ?? config.Text.Morphology);
        var kb = KnowledgeLoader.Load(contentDirectory, lemmatizer);
        return new ChatEngine(kb, config) { MorphologyNote = note };
    }

    /// <summary>Lemmatizers.Create, но с понятной ошибкой ContentException, если морфология нужна, а словарей нет.</summary>
    public static (ILemmatizer Lemmatizer, string? Note) CreateLemmatizer(string mode)
    {
        try
        {
            return Lemmatizers.Create(mode);
        }
        catch (MorphologyUnavailableException ex)
        {
            throw new ContentException($"{ex.Message} Или поставьте в config.json \"text\": {{\"morphology\": \"auto\"}}.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new ContentException(ex.Message, ex);
        }
    }

    public ChatReply Ask(string? userInput)
    {
        var text = (userInput ?? "").Trim();
        if (text.Length == 0)
            return Simple(ReplyKind.NotUnderstood, "Введите вопрос или команду /помощь.");

        var lower = text.ToLowerInvariant();
        if (ExitWords.Contains(lower))
        {
            _session.ResetAll();
            return Simple(ReplyKind.Exit, "До свидания!");
        }

        if (text.StartsWith('/'))
        {
            _session.Reset();
            return HandleCommand(text);
        }

        var pending = _session.Pending;
        if (pending != null && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            if (number == 0)
            {
                _session.ResetAll();
                return EscalateReply(null, null);
            }
            if (number >= 1 && number <= pending.Options.Count)
            {
                _session.Reset();
                return Choose(pending, pending.Options[number - 1]);
            }
        }

        if (pending?.FeedbackQuestionId != null && TryParseFeedback(lower, out var helpful))
            return HandleFeedback(pending, helpful);

        // Любой другой ввод в режиме ожидания выбора считается новым вопросом.
        _session.Reset();
        var lemmas = _text.Lemmatize(text);
        return Respond(_retriever.Search(lemmas, query: text), null, text);
    }

    // ---------- Отзыв об ответе ----------

    private static bool TryParseFeedback(string lower, out bool helpful)
    {
        switch (lower)
        {
            case "да":
            case "+":
            case "помог":
            case "помогло":
                helpful = true;
                return true;
            case "нет":
            case "-":
            case "не помог":
            case "не помогло":
                helpful = false;
                return true;
            default:
                helpful = false;
                return false;
        }
    }

    private ChatReply HandleFeedback(PendingChoice pending, bool helpful)
    {
        FeedbackGiven?.Invoke(new FeedbackEntry(
            DateTime.Now, pending.Query ?? "", pending.FeedbackQuestionId!, helpful));

        if (helpful)
        {
            _session.Reset();
            return Simple(ReplyKind.Feedback, "Спасибо за отзыв!");
        }

        // Ответ не помог: оставляем похожие вопросы и предлагаем переформулировать.
        _session.Wait(pending with { FeedbackQuestionId = null });

        var sb = new StringBuilder("Жаль, что ответ не помог. ");
        if (pending.Options.Count > 0)
        {
            sb.Append("Возможно, поможет один из похожих вопросов (введите номер) или задайте вопрос другими словами:");
            foreach (var o in pending.Options)
                sb.Append('\n').Append($"  {o.Number}. {o.Label}");
        }
        else
        {
            sb.Append("Попробуйте задать вопрос другими словами.");
        }
        sb.Append('\n').Append("  0. Обратиться в техподдержку");
        return new ChatReply(ReplyKind.Feedback, sb.ToString(), pending.Options);
    }

    // ---------- Ответ на вопрос ----------

    private ChatReply Choose(PendingChoice pending, ChatOption option)
    {
        // Выбор продукта или ветки — это навигация по меню, а не неудачная попытка:
        // такие шаги не приближают перевод в поддержку.
        if (option.Product != null)
            return Respond(_retriever.Search(pending.Lemmas, option.Product, pending.Query),
                option.Product, pending.Query, fromMenu: true);

        if (option.Branch != null)
            return Respond(_retriever.Search(pending.Lemmas, option.Branch, pending.Query),
                option.Branch, pending.Query, fromMenu: true);

        var reply = AnswerReply(option.Question!, option.Score, null);
        if (!Guided)
            return reply;

        var others = pending.Options
            .Where(o => o.Question != null && o.Number != option.Number)
            .Select(o => (Question: o.Question!, o.Score));
        return WithFeedback(reply, others, pending.Lemmas, pending.Query);
    }

    private ChatReply Respond(RetrievalResult result, Category? node, string? query, bool fromMenu = false)
    {
        var decision = _policy.Decide(result);
        var debug = DebugEnabled ? BuildDebug(result) : null;

        switch (decision.Kind)
        {
            case DecisionKind.NotUnderstood:
                return Simple(ReplyKind.NotUnderstood, "Не понял вопрос. Попробуйте переформулировать.", debug);

            case DecisionKind.Escalate:
                _session.ResetAll();
                return EscalateReply(result.Scope, debug);

            case DecisionKind.Answer:
            {
                var best = decision.Candidates[0];
                var reply = AnswerReply(best.Question, best.Score, debug);
                if (!Guided)
                    return reply;

                var others = result.Candidates
                    .Skip(1)
                    .Where(c => c.Score >= _config.Dialog.MinScore)
                    .Select(c => (c.Question, c.Score));
                return WithFeedback(reply, others, result.Lemmas, query);
            }

            case DecisionKind.ClarifyProduct:
            {
                var options = decision.Products
                    .Select((p, i) => new ChatOption(i + 1, p.Name, 0, null, p))
                    .ToList();
                _session.Wait(new PendingChoice(ReplyKind.ClarifyProduct, options, result.Lemmas, null, query));

                var sb = new StringBuilder("О каком продукте вы спрашиваете?");
                foreach (var o in options)
                    sb.Append('\n').Append($"  {o.Number}. {o.Label}");
                sb.Append('\n').Append("  0. Другое / связаться с поддержкой");
                sb.Append('\n').Append("Введите номер:");
                return new ChatReply(ReplyKind.ClarifyProduct, sb.ToString(), options, debug);
            }

            default:
            {
                // Clarify или ClarifyBranch. В управляемом диалоге бот уточняет несколько раз,
                // и только потом направляет в поддержку. Переходы по меню тем (fromMenu) не считаются.
                if (Guided && !fromMenu)
                {
                    if (_session.Steps >= _config.Dialog.EscalateAfterSteps)
                    {
                        _session.ResetAll();
                        return EscalateReply(result.Scope ?? node, debug);
                    }
                    _session.NextStep();
                }

                if (decision.Kind == DecisionKind.ClarifyBranch)
                {
                    // Свободный вопрос без явного совпадения: сначала предложить близкие варианты,
                    // и только если их нет — меню тем. При переходе по меню тем показываем ветку целиком.
                    if (node == null && !fromMenu)
                    {
                        var fallback = SuggestionMenu(result, query, debug);
                        if (fallback != null)
                            return fallback;
                    }
                    return BranchReply(node ?? result.Scope, result, query, debug);
                }

                var options = decision.Candidates
                    .Select((c, i) => new ChatOption(i + 1, c.Question.Title, c.Score, c.Question, null))
                    .ToList();
                _session.Wait(new PendingChoice(ReplyKind.Clarify, options, result.Lemmas, null, query));

                var sb = new StringBuilder();
                if (options.Count == 1)
                {
                    sb.Append($"Возможно, вы спрашиваете: «{options[0].Label}»?");
                    sb.Append('\n').Append($"  1. Да, показать ответ  (score: {Fmt(options[0].Score)})");
                    sb.Append('\n').Append("  0. Нет / связаться с поддержкой");
                }
                else
                {
                    sb.Append("Уточните, что вы имеете в виду?");
                    foreach (var o in options)
                        sb.Append('\n').Append($"  {o.Number}. {o.Label}  (score: {Fmt(o.Score)})");
                    sb.Append('\n').Append("  0. Другое / связаться с поддержкой");
                }
                sb.Append('\n').Append("Введите номер:");
                return new ChatReply(ReplyKind.Clarify, sb.ToString(), options, debug);
            }
        }
    }

    // ---------- Похожие вопросы вместо меню тем ----------

    /// <summary>
    /// Явного совпадения нет: показать близкие варианты вместо меню тем — вопросы, которые
    /// по ключам почти дотянули до порога (оценка не ниже dialog.suggestScore).
    /// Возвращает null, если предложить нечего.
    /// </summary>
    private ChatReply? SuggestionMenu(RetrievalResult result, string? query, string? debug)
    {
        if (result.Lemmas.Count == 0)
            return null;

        var pool = _kb.Questions.Where(q => q.IsActive).ToDictionary(q => q.Id);
        var chosen = new List<(string Id, double Score)>();

        var threshold = _config.Dialog.SuggestScore;
        foreach (var c in result.Candidates)
        {
            if (threshold > 0 && c.Score >= threshold && pool.ContainsKey(c.Question.Id))
                chosen.Add((c.Question.Id, c.Score));
        }

        chosen = chosen.Take(_config.Menu.MaxOptions).ToList();
        if (chosen.Count == 0)
            return null;

        var options = chosen
            .Select((x, i) => new ChatOption(i + 1, pool[x.Id].Title, x.Score, pool[x.Id], null))
            .ToList();
        _session.Wait(new PendingChoice(ReplyKind.Clarify, options, result.Lemmas, null, query));

        var sb = new StringBuilder("Точного совпадения не нашёл. Возможно, вы имели в виду:");
        foreach (var o in options)
            sb.Append('\n').Append($"  {o.Number}. {o.Label}");
        sb.Append('\n').Append("  0. Ничего не подходит — обратиться в техподдержку");
        sb.Append('\n').Append("Введите номер:");
        return new ChatReply(ReplyKind.Clarify, sb.ToString(), options, debug);
    }

    // ---------- Уточнение по веткам дерева ----------

    /// <summary>
    /// Предлагает выбрать тему (или блок), а на последнем уровне — конкретный вопрос.
    /// Ветки с единственным вложенным разделом пропускаются.
    /// </summary>
    private ChatReply BranchReply(Category? node, RetrievalResult result, string? query, string? debug)
    {
        var lemmas = result.Lemmas;
        var prefix = _config.Scoring.PrefixMinLength;
        var max = _config.Dialog.MaxBranchOptions;

        var children = ChildrenWithQuestions(node);
        while (children.Count == 1)
        {
            node = children[0];
            children = ChildrenWithQuestions(node);
        }

        if (children.Count == 0)
        {
            var pool = node == null ? _kb.Questions : _kb.QuestionsUnder(node.Id);
            var ranked = pool
                .Where(q => q.IsActive)
                .Select(q => (Question: q, Score: Math.Max(
                    Scorer.Evaluate(q.Keywords, lemmas, prefix, Fuzzy).Score,
                    result.Semantic.GetValueOrDefault(q.Id))))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Question.Id, IdComparer.Instance)
                .Take(max)
                .ToList();
            var questionOptions = ranked
                .Select((x, i) => new ChatOption(i + 1, x.Question.Title, x.Score, x.Question, null))
                .ToList();
            _session.Wait(new PendingChoice(ReplyKind.ClarifyBranch, questionOptions, lemmas, null, query));

            var sb = new StringBuilder(node == null
                ? "Выберите подходящий вопрос:"
                : $"В разделе «{node.Name}» есть такие вопросы. Выберите подходящий:");
            foreach (var o in questionOptions)
                sb.Append('\n').Append($"  {o.Number}. {o.Label}");
            sb.Append('\n').Append("  0. Ничего не подходит — обратиться в техподдержку");
            sb.Append('\n').Append("Введите номер:");
            return new ChatReply(ReplyKind.ClarifyBranch, sb.ToString(), questionOptions, debug);
        }

        var branchOptions = children
            .Select(c => (Category: c, Score: BranchScore(c, lemmas, result.Semantic)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Category.Id, IdComparer.Instance)
            .Take(max)
            .Select((x, i) => new ChatOption(i + 1, x.Category.Name, x.Score, null, null, x.Category))
            .ToList();
        _session.Wait(new PendingChoice(ReplyKind.ClarifyBranch, branchOptions, lemmas, null, query));

        var text = new StringBuilder(node == null
            ? "Уточните, к какой теме относится ваш вопрос:"
            : $"Уточните, к какой теме относится ваш вопрос ({node.Name}):");
        foreach (var o in branchOptions)
            text.Append('\n').Append($"  {o.Number}. {o.Label}");
        text.Append('\n').Append("  0. Ничего не подходит — обратиться в техподдержку");
        text.Append('\n').Append("Введите номер:");
        return new ChatReply(ReplyKind.ClarifyBranch, text.ToString(), branchOptions, debug);
    }

    private List<Category> ChildrenWithQuestions(Category? node)
    {
        IEnumerable<Category> children = node == null ? _kb.Roots : node.Children;
        return children.Where(c => _kb.QuestionsUnder(c.Id).Any(q => q.IsActive)).ToList();
    }

    /// <summary>Оценка ветки: лучшее из ключей самой ветки, ключей её вопросов и смысловой оценки вопросов.</summary>
    private double BranchScore(Category category, IReadOnlyList<string> lemmas,
        IReadOnlyDictionary<string, double> semantic)
    {
        var prefix = _config.Scoring.PrefixMinLength;
        var best = _kb.QuestionsUnder(category.Id)
            .Where(q => q.IsActive)
            .Select(q => Math.Max(Scorer.Evaluate(q.Keywords, lemmas, prefix, Fuzzy).Score,
                semantic.GetValueOrDefault(q.Id)))
            .DefaultIfEmpty(0.0)
            .Max();
        var own = category.Keywords.Count > 0
            ? Scorer.Evaluate(category.Keywords, lemmas, prefix, Fuzzy).Score
            : 0.0;
        return Math.Max(best, own);
    }

    // ---------- Ответ с похожими вопросами и отзывом ----------

    /// <summary>Добавляет под ответом похожие вопросы, просьбу оценить ответ и пункт «0. Техподдержка».</summary>
    private ChatReply WithFeedback(
        ChatReply reply,
        IEnumerable<(Question Question, double Score)> others,
        IReadOnlyList<string> lemmas,
        string? query)
    {
        var options = others
            .Take(_config.Menu.MaxOptions)
            .Select((x, i) => new ChatOption(i + 1, x.Question.Title, x.Score, x.Question, null))
            .ToList();

        var feedbackId = _config.Dialog.Feedback ? reply.Question?.Id : null;
        _session.Wait(new PendingChoice(ReplyKind.Clarify, options, lemmas, feedbackId, query));
        _session.ResetSteps();

        var sb = new StringBuilder(reply.Text).Append("\n\n");
        if (options.Count > 0)
        {
            sb.Append("Не то, что искали? Похожие вопросы:");
            foreach (var o in options)
                sb.Append('\n').Append($"  {o.Number}. {o.Label}");
            sb.Append('\n');
        }
        if (_config.Dialog.Feedback)
            sb.Append("Ответ помог? Напишите «да» или «нет».").Append('\n');
        sb.Append("  0. Обратиться в техподдержку").Append('\n');
        sb.Append("Введите номер или задайте новый вопрос:");
        return reply with { Text = sb.ToString(), Options = options };
    }

    private ChatReply AnswerReply(Question question, double score, string? debug)
    {
        _kb.Categories.TryGetValue(question.CategoryId, out var section);
        var text = _composer.Compose(question, score, section);
        return new ChatReply(ReplyKind.Answer, text, Array.Empty<ChatOption>(), debug, question, score);
    }

    private ChatReply EscalateReply(Category? scope, string? debug)
    {
        var text = $"Не нашёл ответа. Обратитесь в {_config.Support.Url}";
        if (scope != null)
        {
            var holder = _kb.NearestAncestorOrSelf(scope.Id, c => !string.IsNullOrEmpty(c.Contact));
            if (holder != null)
                text += $"\nПо вопросам «{holder.Name}»: {holder.Contact}";
        }
        return new ChatReply(ReplyKind.Escalate, text, Array.Empty<ChatOption>(), debug);
    }

    // ---------- Команды ----------

    private ChatReply HandleCommand(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : "";

        switch (command)
        {
            case "/помощь":
            case "/help":
                return Simple(ReplyKind.Command,
                    "Напишите свой вопрос обычным текстом. Команды:\n" +
                    "  /темы            показать разделы базы знаний\n" +
                    "  /темы 1.2        показать вопросы раздела\n" +
                    "  /отладка вкл     показывать, как бот ищет ответ\n" +
                    "  /отладка выкл    выключить отладку\n" +
                    "  /сессия          номер текущей беседы\n" +
                    "  /помощь          эта подсказка\n" +
                    "  выход            завершить работу");

            case "/темы":
            case "/topics":
                return argument.Length == 0 ? TopicsTree() : TopicQuestions(argument);

            case "/сессия":
            case "/session":
                return Simple(ReplyKind.Command, SessionId != null
                    ? $"Номер сессии: {SessionId}"
                    : "Журнал бесед выключен (config.json → sessions.enabled).");

            case "/отладка":
            case "/debug":
                switch (argument.ToLowerInvariant())
                {
                    case "вкл":
                    case "on":
                        DebugEnabled = true;
                        return Simple(ReplyKind.Command, "Отладка включена.");
                    case "выкл":
                    case "off":
                        DebugEnabled = false;
                        return Simple(ReplyKind.Command, "Отладка выключена.");
                    default:
                        return Simple(ReplyKind.Command,
                            $"Отладка сейчас {(DebugEnabled ? "включена" : "выключена")}. Команды: /отладка вкл, /отладка выкл.");
                }

            default:
                return Simple(ReplyKind.Command, "Неизвестная команда. Список команд: /помощь");
        }
    }

    private ChatReply TopicsTree()
    {
        var sb = new StringBuilder("Разделы базы знаний:");

        void Print(Category category, int level)
        {
            var count = _kb.QuestionsUnder(category.Id).Count();
            sb.Append('\n').Append(new string(' ', 2 + level * 2)).Append($"{category.Id} {category.Name} ({count})");
            foreach (var child in category.Children)
                Print(child, level + 1);
        }

        foreach (var root in _kb.Roots)
            Print(root, 0);

        sb.Append('\n').Append("Вопросы раздела: /темы <номер>, например /темы 1.2");
        return Simple(ReplyKind.Command, sb.ToString());
    }

    private ChatReply TopicQuestions(string id)
    {
        if (!_kb.Categories.TryGetValue(id, out var category))
            return Simple(ReplyKind.Command, $"Раздел «{id}» не найден. Список разделов: /темы");

        var questions = _kb.QuestionsUnder(id).OrderBy(q => q.Id, IdComparer.Instance).ToList();
        if (questions.Count == 0)
            return Simple(ReplyKind.Command, $"В разделе «{category.Name}» пока нет вопросов.");

        var sb = new StringBuilder($"{category.Id} {category.Name}:");
        foreach (var q in questions)
            sb.Append('\n').Append($"  {q.Id}  {q.Title}");
        return Simple(ReplyKind.Command, sb.ToString());
    }

    // ---------- Вспомогательное ----------

    private static ChatReply Simple(ReplyKind kind, string text, string? debug = null) =>
        new(kind, text, Array.Empty<ChatOption>(), debug);

    public static string Fmt(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private string BuildDebug(RetrievalResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"--- леммы: [{string.Join(", ", result.Lemmas)}]");
        sb.Append('\n').Append(result.Scope == null
            ? "--- область: не определена"
            : $"--- область: {result.Scope.Id} {result.Scope.Name} (score {Fmt(result.ScopeScore)})");

        var prefix = _config.Scoring.PrefixMinLength;
        foreach (var c in result.Candidates)
        {
            var typos = Scorer.TypoMatches(c.Question.Keywords, result.Lemmas, prefix, Fuzzy);
            var note = typos.Count > 0
                ? "  (опечатка: " + string.Join(", ", typos.Select(t => $"{t.Lemma}≈{t.Keyword}")) + ")"
                : "";
            if (c.Cosine != null)
                note += $"  [ключи {Fmt(c.KeywordScore)}, смысл: похожесть {Fmt(c.Cosine.Value)}]";
            sb.Append('\n').Append($"  {c.Question.Id} \"{c.Question.Title}\" → {Fmt(c.Score)}{note}");
        }

        sb.Append('\n').Append($"--- шагов уточнения без ответа: {_session.Steps}; морфология: {Morphology}; " +
                               $"опечатки: {Fuzzy?.Name ?? "выкл"}; смысл: выкл");
        return sb.ToString();
    }
}
