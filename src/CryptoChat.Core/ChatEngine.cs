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
    private readonly KnowledgeBase _kb;
    private readonly AppConfig _config;
    private readonly ITextProcessor _text;
    private readonly IRetriever _retriever;
    private readonly IDialogPolicy _policy;
    private readonly IAnswerComposer _composer;
    private readonly ChatSession _session = new();

    public bool DebugEnabled { get; set; }
    public KnowledgeBase KnowledgeBase => _kb;
    public AppConfig Config => _config;

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
        _text = text ?? new TextProcessor(synonyms: kb.Synonyms);
        _retriever = retriever ?? new KeywordRetriever(kb, config);
        _policy = policy ?? new DialogPolicy(config, kb);
        _composer = composer ?? new TemplateComposer();
    }

    /// <summary>Загружает и проверяет каталог content/ и создаёт готовый движок.</summary>
    public static ChatEngine Create(string contentDirectory)
    {
        var kb = KnowledgeLoader.Load(contentDirectory);
        var config = AppConfig.Load(contentDirectory);
        return new ChatEngine(kb, config);
    }

    public ChatReply Ask(string? userInput)
    {
        var text = (userInput ?? "").Trim();
        if (text.Length == 0)
            return Simple(ReplyKind.NotUnderstood, "Введите вопрос или команду /помощь.");

        var lower = text.ToLowerInvariant();
        if (lower is "выход" or "exit" or "quit" or "/выход" or "/exit" or "/quit")
        {
            _session.Reset();
            return Simple(ReplyKind.Exit, "До свидания!");
        }

        if (text.StartsWith('/'))
        {
            _session.Reset();
            return HandleCommand(text);
        }

        var pending = _session.Pending;
        if (pending != null && int.TryParse(text, out var number))
        {
            if (number == 0)
            {
                _session.Reset();
                return EscalateReply(null, null);
            }
            if (number >= 1 && number <= pending.Options.Count)
            {
                _session.Reset();
                return Choose(pending, pending.Options[number - 1]);
            }
        }

        // Любой другой ввод в режиме ожидания выбора считается новым вопросом.
        _session.Reset();
        var lemmas = _text.Lemmatize(text);
        return Respond(_retriever.Search(lemmas));
    }

    // ---------- Ответ на вопрос ----------

    private ChatReply Choose(PendingChoice pending, ChatOption option)
    {
        if (option.Product != null)
            return Respond(_retriever.Search(pending.Lemmas, option.Product));

        return AnswerReply(option.Question!, option.Score, null);
    }

    private ChatReply Respond(RetrievalResult result)
    {
        var decision = _policy.Decide(result);
        var debug = DebugEnabled ? BuildDebug(result) : null;

        switch (decision.Kind)
        {
            case DecisionKind.NotUnderstood:
                return Simple(ReplyKind.NotUnderstood, "Не понял вопрос. Попробуйте переформулировать.", debug);

            case DecisionKind.Escalate:
                return EscalateReply(result.Scope, debug);

            case DecisionKind.Answer:
                return AnswerReply(decision.Candidates[0].Question, decision.Candidates[0].Score, debug);

            case DecisionKind.ClarifyProduct:
            {
                var options = decision.Products
                    .Select((p, i) => new ChatOption(i + 1, p.Name, 0, null, p))
                    .ToList();
                _session.Wait(new PendingChoice(ReplyKind.ClarifyProduct, options, result.Lemmas));

                var sb = new StringBuilder("О каком продукте вы спрашиваете?");
                foreach (var o in options)
                    sb.Append('\n').Append($"  {o.Number}. {o.Label}");
                sb.Append('\n').Append("  0. Другое / связаться с поддержкой");
                sb.Append('\n').Append("Введите номер:");
                return new ChatReply(ReplyKind.ClarifyProduct, sb.ToString(), options, debug);
            }

            default:
            {
                var options = decision.Candidates
                    .Select((c, i) => new ChatOption(i + 1, c.Question.Title, c.Score, c.Question, null))
                    .ToList();
                _session.Wait(new PendingChoice(ReplyKind.Clarify, options, result.Lemmas));

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
                    "  /помощь          эта подсказка\n" +
                    "  выход            завершить работу");

            case "/темы":
            case "/topics":
                return argument.Length == 0 ? TopicsTree() : TopicQuestions(argument);

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

    private static string Fmt(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string BuildDebug(RetrievalResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"--- леммы: [{string.Join(", ", result.Lemmas)}]");
        sb.Append('\n').Append(result.Scope == null
            ? "--- область: не определена"
            : $"--- область: {result.Scope.Id} {result.Scope.Name} (score {Fmt(result.ScopeScore)})");
        foreach (var c in result.Candidates)
            sb.Append('\n').Append($"  {c.Question.Id} \"{c.Question.Title}\" → {Fmt(c.Score)}");
        return sb.ToString();
    }
}
