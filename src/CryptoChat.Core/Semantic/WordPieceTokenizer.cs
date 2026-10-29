using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CryptoChat.Core.Semantic;

/// <summary>
/// Токенизатор BERT (WordPiece) — перенос правил библиотеки HuggingFace tokenizers, которой пользуется
/// sentence-transformers в Python-версии. Настройки и словарь читаются из того же файла tokenizer.json.
///
/// Порядок как в tokenizers: очистка текста → пробелы вокруг иероглифов → (удаление ударений) →
/// (нижний регистр) → разбиение по пробелам и знакам препинания → WordPiece → [CLS] … [SEP].
/// </summary>
public sealed class WordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly bool _cleanText;
    private readonly bool _handleChineseChars;
    private readonly bool _stripAccents;
    private readonly bool _lowercase;
    private readonly string _unkToken;
    private readonly string _prefix;
    private readonly int _maxInputCharsPerWord;
    private readonly int? _clsId; // null — служебные токены не добавляются (в tokenizer.json нет post_processor)
    private readonly int? _sepId;

    private WordPieceTokenizer(Dictionary<string, int> vocab, bool cleanText, bool handleChineseChars,
        bool stripAccents, bool lowercase, string unkToken, string prefix, int maxInputCharsPerWord,
        int? clsId, int? sepId)
    {
        _vocab = vocab;
        _cleanText = cleanText;
        _handleChineseChars = handleChineseChars;
        _stripAccents = stripAccents;
        _lowercase = lowercase;
        _unkToken = unkToken;
        _prefix = prefix;
        _maxInputCharsPerWord = maxInputCharsPerWord;
        _clsId = clsId;
        _sepId = sepId;
    }

    /// <summary>Читает tokenizer.json (формат HuggingFace tokenizers). Поддерживается только токенизатор BERT.</summary>
    public static WordPieceTokenizer Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;

        var normalizer = root.GetProperty("normalizer");
        Require(normalizer.ValueKind == JsonValueKind.Object && Type(normalizer) == "BertNormalizer",
            "нормализатор текста не BertNormalizer");
        var lowercase = normalizer.GetProperty("lowercase").GetBoolean();
        var strip = normalizer.TryGetProperty("strip_accents", out var s) && s.ValueKind != JsonValueKind.Null
            ? s.GetBoolean()
            : lowercase; // как в tokenizers: по умолчанию ударения снимаются вместе с нижним регистром

        var preTokenizer = root.GetProperty("pre_tokenizer");
        Require(preTokenizer.ValueKind == JsonValueKind.Object && Type(preTokenizer) == "BertPreTokenizer",
            "разбиение на слова не BertPreTokenizer");

        var model = root.GetProperty("model");
        Require(Type(model) == "WordPiece", "модель токенизатора не WordPiece");
        var vocab = new Dictionary<string, int>();
        foreach (var item in model.GetProperty("vocab").EnumerateObject())
            vocab[item.Name] = item.Value.GetInt32();

        var (clsId, sepId) = SpecialTokens(root.GetProperty("post_processor"), vocab);

        return new WordPieceTokenizer(vocab,
            normalizer.GetProperty("clean_text").GetBoolean(),
            normalizer.GetProperty("handle_chinese_chars").GetBoolean(),
            strip, lowercase,
            model.GetProperty("unk_token").GetString()!,
            model.GetProperty("continuing_subword_prefix").GetString()!,
            model.GetProperty("max_input_chars_per_word").GetInt32(),
            clsId, sepId);
    }

    private static string? Type(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("type", out var type) ? type.GetString() : null;

    private static void Require(bool condition, string what)
    {
        if (!condition)
            throw new SemanticUnavailableException($"Неподдерживаемый токенизатор: {what}.");
    }

    private static (int? Cls, int? Sep) SpecialTokens(JsonElement post, Dictionary<string, int> vocab)
    {
        if (post.ValueKind == JsonValueKind.Null)
            return (null, null);
        switch (Type(post))
        {
            case "BertProcessing":
                return (post.GetProperty("cls")[1].GetInt32(), post.GetProperty("sep")[1].GetInt32());
            case "TemplateProcessing":
            {
                // Ожидается шаблон «[CLS] $A [SEP]».
                var single = post.GetProperty("single").EnumerateArray().ToList();
                Require(single.Count == 3
                        && single[0].TryGetProperty("SpecialToken", out var first)
                        && single[1].TryGetProperty("Sequence", out _)
                        && single[2].TryGetProperty("SpecialToken", out var last),
                    "шаблон добавления служебных токенов не «[CLS] текст [SEP]»");
                var cls = single[0].GetProperty("SpecialToken").GetProperty("id").GetString()!;
                var sep = single[2].GetProperty("SpecialToken").GetProperty("id").GetString()!;
                var specials = post.GetProperty("special_tokens");
                return (specials.GetProperty(cls).GetProperty("ids")[0].GetInt32(),
                        specials.GetProperty(sep).GetProperty("ids")[0].GetInt32());
            }
            default:
                Require(false, $"правило добавления служебных токенов «{Type(post)}»");
                return (null, null);
        }
    }

    /// <summary>Номера токенов текста вместе с [CLS] и [SEP]; не длиннее maxLength (как truncation в tokenizers).</summary>
    public IReadOnlyList<int> Encode(string text, int maxLength = int.MaxValue)
    {
        var ids = new List<int>();
        foreach (var word in PreTokenize(Normalize(text)))
            ids.AddRange(WordPiece(word));

        var specials = _clsId != null ? 2 : 0;
        var limit = Math.Max(0, maxLength - specials);
        if (ids.Count > limit)
            ids.RemoveRange(limit, ids.Count - limit);
        if (_clsId != null)
        {
            ids.Insert(0, _clsId.Value);
            ids.Add(_sepId!.Value);
        }
        return ids;
    }

    // ---------- Нормализация (BertNormalizer) ----------

    internal string Normalize(string text)
    {
        var runes = text.EnumerateRunes().ToList();

        if (_cleanText)
            runes = runes
                .Where(r => !(r.Value == 0 || r.Value == 0xFFFD || IsControl(r)))
                .Select(r => IsWhitespace(r) ? new Rune(' ') : r)
                .ToList();

        if (_handleChineseChars)
            runes = runes.SelectMany(r => IsChineseChar(r.Value) ? new[] { new Rune(' '), r, new Rune(' ') } : new[] { r })
                .ToList();

        var result = string.Concat(runes.Select(r => r.ToString()));

        if (_stripAccents)
            result = string.Concat(result.Normalize(NormalizationForm.FormD)
                .EnumerateRunes()
                .Where(r => Rune.GetUnicodeCategory(r) != UnicodeCategory.NonSpacingMark)
                .Select(r => r.ToString()));

        if (_lowercase)
            result = string.Concat(result.EnumerateRunes().Select(Rune.ToLowerInvariant).Select(r => r.ToString()));

        return result;
    }

    /// <summary>Пробельный символ как в Rust (char::is_whitespace) плюс \t, \n, \r.</summary>
    internal static bool IsWhitespace(Rune r) =>
        r.Value is '\t' or '\n' or '\r' || Rune.IsWhiteSpace(r);

    /// <summary>
    /// Управляющий символ: категории Cc, Cf и Co (кроме \t, \n, \r). Неназначенные символы (Cn) tokenizers
    /// на деле не удаляет, хотя в комментарии к коду написано иначе, — проверено на всех символах Unicode.
    /// </summary>
    internal static bool IsControl(Rune r)
    {
        if (r.Value is '\t' or '\n' or '\r')
            return false;
        return Rune.GetUnicodeCategory(r) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse
               && !InRanges(r.Value, FormatUnknownToTokenizers);
    }

    // В tokenizers таблицы Unicode старее, чем в .NET. Символы, которые появились или сменили категорию
    // позже, учитываются по-старому — так токены совпадают с Python на всех символах Unicode.

    /// <summary>Знаки форматирования (Cf), которых tokenizers не знает и потому не удаляет.</summary>
    private static readonly (int From, int To)[] FormatUnknownToTokenizers =
    {
        (0x0890, 0x0891), (0x08E2, 0x08E2), (0x110CD, 0x110CD), (0x13430, 0x1343F),
    };

    /// <summary>Знаки препинания (P*), которых tokenizers не знает.</summary>
    private static readonly (int From, int To)[] PunctuationUnknownToTokenizers =
    {
        (0x061D, 0x061D), (0x09FD, 0x09FD), (0x0A76, 0x0A76), (0x0C77, 0x0C77), (0x0C84, 0x0C84), (0x1B4E, 0x1B4F),
        (0x1B7D, 0x1B7F), (0x2E43, 0x2E4F), (0x2E52, 0x2E5D), (0x10D6E, 0x10D6E), (0x10EAD, 0x10EAD), (0x10F55, 0x10F59),
        (0x10F86, 0x10F89), (0x113D4, 0x113D5), (0x113D7, 0x113D8), (0x1144B, 0x1144F), (0x1145A, 0x1145B),
        (0x1145D, 0x1145D), (0x11660, 0x1166C), (0x116B9, 0x116B9), (0x1183B, 0x1183B), (0x11944, 0x11946),
        (0x119E2, 0x119E2), (0x11A3F, 0x11A46), (0x11A9A, 0x11A9C), (0x11A9E, 0x11AA2), (0x11B00, 0x11B09),
        (0x11BE1, 0x11BE1), (0x11C41, 0x11C45), (0x11C70, 0x11C71), (0x11EF7, 0x11EF8), (0x11F43, 0x11F4F),
        (0x11FFF, 0x11FFF), (0x12FF1, 0x12FF2), (0x16D6D, 0x16D6F), (0x16E97, 0x16E9A), (0x16FE2, 0x16FE2),
        (0x1E5FF, 0x1E5FF), (0x1E95E, 0x1E95F),
    };

    /// <summary>Символы, которые в старых таблицах были знаками препинания, а теперь — нет.</summary>
    private static readonly (int From, int To)[] FormerPunctuation = { (0x166D, 0x166D), (0x111C9, 0x111C9) };

    private static bool InRanges(int value, (int From, int To)[] ranges) =>
        ranges.Any(range => value >= range.From && value <= range.To);

    private static bool IsChineseChar(int c) =>
        c is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0x20000 and <= 0x2A6DF
            or >= 0x2A700 and <= 0x2B73F or >= 0x2B740 and <= 0x2B81F or >= 0x2B920 and <= 0x2CEAF
            or >= 0xF900 and <= 0xFAFF or >= 0x2F800 and <= 0x2FA1F;

    // ---------- Разбиение на слова (BertPreTokenizer) ----------

    private static IEnumerable<string> PreTokenize(string text)
    {
        var current = new StringBuilder();
        foreach (var r in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(r))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
            }
            else if (IsPunctuation(r))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return r.ToString();
            }
            else
            {
                current.Append(r.ToString());
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>Знак препинания: любой ASCII-знак (в том числе $, +, &lt;, =, &gt;, ^, `, |, ~) или категория Unicode P*.</summary>
    internal static bool IsPunctuation(Rune r) =>
        InRanges(r.Value, FormerPunctuation) || IsPunctuationBase(r) && !InRanges(r.Value, PunctuationUnknownToTokenizers);

    internal static bool IsPunctuationBase(Rune r)
    {
        if (r.Value < 128)
            return r.Value is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126;
        return Rune.GetUnicodeCategory(r) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
    }

    // ---------- WordPiece ----------

    private IEnumerable<int> WordPiece(string word)
    {
        var runes = word.EnumerateRunes().Select(r => r.ToString()).ToArray();
        if (runes.Length > _maxInputCharsPerWord)
            return new[] { _vocab[_unkToken] };

        var pieces = new List<int>();
        var start = 0;
        while (start < runes.Length)
        {
            var end = runes.Length;
            int? found = null;
            while (start < end)
            {
                var piece = string.Concat(runes[start..end]);
                if (start > 0)
                    piece = _prefix + piece;
                if (_vocab.TryGetValue(piece, out var id))
                {
                    found = id;
                    break;
                }
                end--;
            }
            if (found == null)
                return new[] { _vocab[_unkToken] };
            pieces.Add(found.Value);
            start = end;
        }
        return pieces;
    }
}
