using System.Text.RegularExpressions;

namespace CryptoChat.Core.Text.Morphology;

/// <summary>
/// Один вариант разбора слова: словоформа, тег, начальная форма и оценка.
/// ParaKey и Stack описывают, каким способом получен разбор: по ним pymorphy3 отсеивает повторы.
/// </summary>
internal sealed record MorphParse(string Word, MorphTag Tag, string NormalForm, double Score, string? ParaKey, string Stack)
{
    public string SeenKey => $"{Word}\u0001{Tag.Key}\u0001{ParaKey ?? "\u0002"}";
}

/// <summary>
/// Морфологический анализатор — перенос MorphAnalyzer из pymorphy3 (MIT) с теми же словарями,
/// поэтому начальная форма слова получается та же, что в Python-версии бота.
///
/// Слово разбирается по очереди способами (как DEFAULT_UNITS для русского языка):
///   словарь → число → пунктуация → римское число / латиница → частица через дефис («-то», «-ка») →
///   наречие «по-…» → слово через дефис → известная приставка («псевдо» + кошка) →
///   неизвестная приставка / похожее окончание → «неизвестное слово».
/// Как только способ из «завершающей» группы дал результат, следующие не пробуются.
/// Затем разборы упорядочиваются по вероятности P(t|w) из корпуса OpenCorpora.
///
/// Разбор инициалов (одна заглавная буква) не перенесён: бот передаёт сюда только слова
/// в нижнем регистре длиной от двух букв.
/// </summary>
internal sealed class MorphAnalyzer
{
    // Настройки из pymorphy3/lang/ru/config.py.
    private static readonly string[] ParticlesAfterHyphen = { "-то", "-ка", "-таки", "-де", "-тко", "-тка", "-с", "-ста" };
    private static readonly IReadOnlyDictionary<int, int[]> CharSubstitutes =
        new Dictionary<int, int[]> { ['е'] = new int[] { 'ё' } };

    private static readonly string[] KnownPrefixes =
    {
        "авиа", "авто", "аква", "анти", "анти-", "антропо", "архи", "арт", "арт-", "астро", "аудио", "аэро",
        "без", "бес", "био", "вело", "взаимо", "вне", "внутри", "видео", "вице-", "вперед", "впереди",
        "гекто", "гелио", "гео", "гетеро", "гига", "гигро", "гипер", "гипо", "гомо", "дву", "двух", "де",
        "дез", "дека", "деци", "дис", "до", "евро", "за", "зоо", "интер", "инфра", "квази", "квази-", "кило",
        "кино", "контр", "контр-", "космо", "космо-", "крипто", "лейб-", "лже", "лже-", "макро", "макси",
        "макси-", "мало", "меж", "медиа", "медиа-", "мега", "мета", "мета-", "метео", "метро", "микро",
        "милли", "мини", "мини-", "моно", "мото", "много", "мульти", "нано", "нарко", "не", "небез", "недо",
        "нейро", "нео", "низко", "обер-", "обще", "одно", "около", "орто", "палео", "пан", "пара", "пента",
        "пере", "пиро", "поли", "полу", "после", "пост", "пост-", "порно", "пра", "пра-", "пред", "пресс-",
        "противо", "противо-", "прото", "псевдо", "псевдо-", "радио", "разно", "ре", "ретро", "ретро-",
        "само", "санти", "сверх", "сверх-", "спец", "суб", "супер", "супер-", "супра", "теле", "тетра",
        "топ-", "транс", "транс-", "ультра", "унтер-", "штаб-", "экзо", "эко", "эндо", "эконом-", "экс",
        "экс-", "экстра", "экстра-", "электро", "энерго", "этно",
    };

    private const double ShapeScore = 0.9;
    private const double HyphenParticleMultiplier = 0.9;
    private const double HyphenAdverbMultiplier = 0.7;
    private const double HyphenatedMultiplier = 0.75;
    private const double KnownPrefixMultiplier = 0.75;
    private const int KnownPrefixMinRemainder = 3;
    private const double UnknownPrefixMultiplier = 0.5;
    private const double KnownSuffixMultiplier = 0.5;
    private const int KnownSuffixMinWordLength = 4;

    private static readonly MorphTag NumberIntTag = new("NUMB,intg");
    private static readonly MorphTag NumberRealTag = new("NUMB,real");
    private static readonly MorphTag PunctuationTag = new("PNCT");
    private static readonly MorphTag RomanTag = new("ROMN");
    private static readonly MorphTag LatinTag = new("LATN");
    private static readonly MorphTag AdverbTag = new("ADVB");
    private static readonly MorphTag UnknownTag = new("UNKN");

    private const string Digits = @"\d(?:_?\d)*";
    private static readonly Regex PythonInt = new($@"^\s*[+-]?{Digits}\s*$", RegexOptions.Compiled);
    private static readonly Regex PythonFloat = new(
        $@"^\s*[+-]?(?:(?:{Digits}(?:\.(?:{Digits})?)?|\.{Digits})(?:[eE][+-]?{Digits})?|inf|infinity|nan)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RomanNumber = new(
        "^M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private delegate List<MorphParse> Unit(string word, string wordLower, HashSet<string> seen);

    private readonly MorphDictionary _dict;
    private readonly (Unit Analyze, bool Terminal)[] _units;
    private readonly (int Id, string Prefix)[] _suffixPrefixes;
    private readonly int[] _predictionSplits;

    public MorphDictionary Dictionary => _dict;

    public MorphAnalyzer(MorphDictionary dictionary)
    {
        _dict = dictionary;
        _units = new (Unit, bool)[]
        {
            ((w, l, s) => DictionaryParse(l, "D"), true),
            (NumberParse, true),
            (PunctuationParse, true),
            (RomanParse, false),
            (LatinParse, true),
            (HyphenParticleParse, true),
            (HyphenAdverbParse, true),
            (HyphenatedWordsParse, true),
            (KnownPrefixParse, true),
            (UnknownPrefixParse, false),
            (KnownSuffixParse, true),
            (UnknownParse, true),
        };
        _suffixPrefixes = dictionary.ParadigmPrefixes.Select((p, i) => (i, p)).Reverse().ToArray();
        _predictionSplits = Enumerable.Range(1, dictionary.MaxSuffixLength).Reverse().ToArray();
    }

    /// <summary>Все разборы слова, самые вероятные первыми.</summary>
    public List<MorphParse> Parse(string word)
    {
        var result = new List<MorphParse>();
        var seen = new HashSet<string>();
        var wordLower = word.ToLowerInvariant();

        foreach (var (analyze, terminal) in _units)
        {
            result.AddRange(analyze(word, wordLower, seen));
            if (terminal && result.Count > 0)
                break;
        }

        return ApplyProbabilities(wordLower, result);
    }

    private List<MorphParse> ApplyProbabilities(string wordLower, List<MorphParse> parses)
    {
        if (parses.Count == 0 || _dict.Probabilities == null)
            return parses;

        var probabilities = parses.Select(p => _dict.Probability(wordLower, p.Tag)).ToList();
        if (probabilities.Sum() == 0)
        {
            // Данных P(t|w) нет: оценки нормируются, порядок сохраняется.
            var total = parses.Sum(p => p.Score);
            var k = total == 0 ? 0 : 1.0 / total;
            return parses.Select(p => p with { Score = p.Score * k }).ToList();
        }

        // Оценка заменяется вероятностью; при равенстве сохраняется исходный порядок.
        return parses
            .Select((p, i) => p with { Score = probabilities[i] })
            .OrderByDescending(p => p.Score)
            .ToList();
    }

    private static void AddIfNotSeen(MorphParse parse, List<MorphParse> result, HashSet<string> seen)
    {
        if (seen.Add(parse.SeenKey))
            result.Add(parse);
    }

    // ---------- Словарь ----------

    /// <param name="analyzerName">Кто разбирает: основной словарь («D») или словарь внутри «неизвестной приставки» («UD»).</param>
    private List<MorphParse> DictionaryParse(string wordLower, string analyzerName)
    {
        var result = new List<MorphParse>();
        foreach (var (fixedWord, parses) in _dict.Words.SimilarItems(wordLower, CharSubstitutes))
        {
            foreach (var record in parses)
            {
                int paraId = record[0], idx = record[1];
                var normalForm = _dict.BuildNormalForm(paraId, idx, fixedWord);
                var tag = _dict.BuildTagInfo(paraId, idx);
                result.Add(new MorphParse(fixedWord, tag, normalForm, 1.0, $"P:{paraId}",
                    $"[{analyzerName}|{fixedWord}|{paraId}|{idx}]"));
            }
        }
        return result;
    }

    // ---------- Числа, пунктуация, латиница ----------

    private static List<MorphParse> Shape(string word, string wordLower, MorphTag tag, string name) =>
        new() { new MorphParse(wordLower, tag, wordLower, ShapeScore, null, $"[{name}|{word}]") };

    private List<MorphParse> NumberParse(string word, string wordLower, HashSet<string> seen)
    {
        if (PythonInt.IsMatch(word))
            return Shape(word, wordLower, NumberIntTag, "NUMB");
        if (PythonFloat.IsMatch(word.Replace(',', '.')))
            return Shape(word, wordLower, NumberRealTag, "NUMB");
        return new();
    }

    private List<MorphParse> PunctuationParse(string word, string wordLower, HashSet<string> seen)
    {
        var isPunctuation = word.Length > 0 && !word.All(char.IsWhiteSpace)
                            && word.Where(c => !char.IsWhiteSpace(c)).All(char.IsPunctuation);
        return isPunctuation ? Shape(word, wordLower, PunctuationTag, "PNCT") : new();
    }

    private List<MorphParse> RomanParse(string word, string wordLower, HashSet<string> seen) =>
        word.Length > 0 && RomanNumber.IsMatch(word) ? Shape(word, wordLower, RomanTag, "ROMN") : new();

    private List<MorphParse> LatinParse(string word, string wordLower, HashSet<string> seen)
    {
        var letters = word.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(IsLatinLetter) ? Shape(word, wordLower, LatinTag, "LATN") : new();
    }

    /// <summary>Буква латиницы (в Python: в названии символа Unicode есть слово LATIN).</summary>
    private static bool IsLatinLetter(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        || (c >= 'À' && c <= 'ɏ' && c != '×' && c != '÷')
        || c is >= 'ɐ' and <= 'ʯ'
        || c is >= 'ᴀ' and <= 'ᵿ'
        || c is >= 'Ḁ' and <= 'ỿ'
        || c is >= 'Ⱡ' and <= 'Ɀ'
        || c is >= '꜠' and <= 'ꟿ'
        || c is >= 'ꬰ' and <= '꭯'
        || c is >= 'Ａ' and <= 'Ｚ'
        || c is >= 'ａ' and <= 'ｚ';

    // ---------- Слова через дефис ----------

    /// <summary>«смотри-ка» → «смотри» + «-ка».</summary>
    private List<MorphParse> HyphenParticleParse(string word, string wordLower, HashSet<string> seen)
    {
        var result = new List<MorphParse>();
        if (!wordLower.Contains('-'))
            return result;

        foreach (var particle in ParticlesAfterHyphen)
        {
            if (!wordLower.EndsWith(particle, StringComparison.Ordinal))
                continue;
            var unsuffixed = wordLower[..^particle.Length];
            if (unsuffixed.Length == 0)
                continue;

            foreach (var p in Parse(unsuffixed))
            {
                AddIfNotSeen(new MorphParse(p.Word + particle, p.Tag, p.NormalForm + particle,
                    p.Score * HyphenParticleMultiplier, p.ParaKey, p.Stack + $"[HP|{particle}]"), result, seen);
            }
            // Если слово кончается одной частицей, другой оно кончаться не может.
            break;
        }
        return result;
    }

    /// <summary>Наречия на «по-»: «по-западному».</summary>
    private List<MorphParse> HyphenAdverbParse(string word, string wordLower, HashSet<string> seen)
    {
        if (wordLower.Length < 5 || !wordLower.StartsWith("по-", StringComparison.Ordinal))
            return new();
        if (!Parse(wordLower[3..]).Any(p => p.Tag.Contains("ADJF", "sing", "datv")))
            return new();

        seen.Add("\u0003adverb\u0001" + wordLower); // как в pymorphy3: запоминается весь разбор
        return new() { new MorphParse(wordLower, AdverbTag, wordLower, HyphenAdverbMultiplier, null, $"[ADVB|{word}]") };
    }

    /// <summary>«интернет-магазин» → «интернет-» + магазин; «человек-гора» → человек + гора.</summary>
    private List<MorphParse> HyphenatedWordsParse(string word, string wordLower, HashSet<string> seen)
    {
        if (!ShouldParseHyphenated(wordLower))
            return new();

        var dash = wordLower.IndexOf('-');
        var left = wordLower[..dash];
        var right = wordLower[(dash + 1)..];
        var leftParses = Parse(left);
        var rightParses = Parse(right);

        // Обе части изменяются вместе.
        var result = new List<MorphParse>();
        var rightFeatures = rightParses.Select(p => SimilarityFeatures(p.Tag)).ToList();
        foreach (var l in leftParses)
        {
            if (l.Tag.IsUnknown)
                continue;
            var leftFeatures = SimilarityFeatures(l.Tag);
            for (int i = 0; i < rightParses.Count; i++)
            {
                if (!leftFeatures.SetEquals(rightFeatures[i]))
                    continue;
                var r = rightParses[i];
                result.Add(new MorphParse($"{l.Word}-{r.Word}", l.Tag, $"{l.NormalForm}-{r.NormalForm}",
                    l.Score * HyphenatedMultiplier, $"S:{r.Stack}", $"[HW|t:{l.Stack}|{r.Stack}]"));
            }
        }

        // Левая часть — неизменяемая приставка.
        foreach (var r in rightParses)
        {
            if (r.Tag.IsUnknown)
                continue;
            result.Add(new MorphParse($"{left}-{r.Word}", r.Tag, $"{left}-{r.NormalForm}",
                r.Score * HyphenatedMultiplier, $"S:{r.Stack}", $"[HW|s:{left}|{r.Stack}]"));
        }
        return result;
    }

    private static bool ShouldParseHyphenated(string word)
    {
        if (!word.Contains('-'))
            return false;
        if (word.Trim('-') != word) // слова, которые начинаются или кончаются дефисом, не разбираются
            return false;
        if (word.Count(c => c == '-') != 1) // нужен ровно один дефис в середине слова
            return false;
        return !KnownPrefixes.Any(p => word.StartsWith(p, StringComparison.Ordinal));
    }

    private static HashSet<string> SimilarityFeatures(MorphTag tag)
    {
        var features = new HashSet<string>(tag.Grammemes.Where(MorphTag.FeatureGrammemes.Contains));
        if (features.Remove("gen1")) features.Add("gent");
        if (features.Remove("loc1")) features.Add("loct");
        return features;
    }

    // ---------- Приставки ----------

    /// <summary>«псевдокошка» → «псевдо» + кошка.</summary>
    private List<MorphParse> KnownPrefixParse(string word, string wordLower, HashSet<string> seen)
    {
        var result = new List<MorphParse>();
        var prefixes = KnownPrefixes
            .Where(p => wordLower.StartsWith(p, StringComparison.Ordinal))
            .OrderByDescending(p => p.Length);
        foreach (var prefix in prefixes)
        {
            var unprefixed = wordLower[prefix.Length..];
            if (unprefixed.Length < KnownPrefixMinRemainder)
                continue;

            foreach (var p in Parse(unprefixed))
            {
                if (!p.Tag.IsProductive)
                    continue;
                AddIfNotSeen(new MorphParse(prefix + p.Word, p.Tag, prefix + p.NormalForm,
                    p.Score * KnownPrefixMultiplier, p.ParaKey, p.Stack + $"[KP|{prefix}]"), result, seen);
            }
        }
        return result;
    }

    /// <summary>«байткод» → (байт) + код: разбирается только конец слова.</summary>
    private List<MorphParse> UnknownPrefixParse(string word, string wordLower, HashSet<string> seen)
    {
        var result = new List<MorphParse>();
        var maxSplit = Math.Min(5, wordLower.Length - 3);
        for (int i = 1; i <= maxSplit; i++)
        {
            var prefix = wordLower[..i];
            foreach (var p in DictionaryParse(wordLower[i..], "UD"))
            {
                if (!p.Tag.IsProductive)
                    continue;
                AddIfNotSeen(new MorphParse(prefix + p.Word, p.Tag, prefix + p.NormalForm,
                    p.Score * UnknownPrefixMultiplier, p.ParaKey, p.Stack + $"[UP|{prefix}]"), result, seen);
            }
        }
        return result;
    }

    // ---------- Похожее окончание ----------

    /// <summary>«бутявкать» → разбирается как слова на «…вкать».</summary>
    private List<MorphParse> KnownSuffixParse(string word, string wordLower, HashSet<string> seen)
    {
        var found = new List<(int Count, string FixedWord, MorphTag Tag, string NormalForm, int PrefixId, string ParaKey, string Stack)>();
        if (word.Length < KnownSuffixMinWordLength)
            return new();

        var totalCounts = Enumerable.Repeat(1, _suffixPrefixes.Length).ToArray();

        foreach (var (prefixId, prefix) in _suffixPrefixes)
        {
            if (!wordLower.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var suffixes = _dict.PredictionSuffixes[prefixId];

            foreach (var i in _predictionSplits)
            {
                var wordStart = MorphDictionary.PySlice(wordLower, 0, wordLower.Length - i);
                var wordEnd = MorphDictionary.PySlice(wordLower, wordLower.Length - i, wordLower.Length);

                foreach (var (fixedSuffix, parses) in suffixes.SimilarItems(wordEnd, CharSubstitutes))
                {
                    var fixedWord = wordStart + fixedSuffix;
                    foreach (var record in parses)
                    {
                        int count = record[0], paraId = record[1], idx = record[2];
                        var tag = _dict.BuildTagInfo(paraId, idx);
                        if (!tag.IsProductive)
                            continue;

                        totalCounts[prefixId] += count;

                        var parse = new MorphParse(fixedWord, tag, "", 0, $"P:{paraId}", "");
                        if (!seen.Add(parse.SeenKey))
                            continue;

                        var normalForm = _dict.BuildNormalForm(paraId, idx, fixedWord);
                        found.Add((count, fixedWord, tag, normalForm, prefixId, $"P:{paraId}",
                            $"[FD|{fixedWord}|{paraId}|{idx}][KS|{fixedSuffix}]"));
                    }
                }

                if (totalCounts[prefixId] > 1)
                    break;
            }
        }

        return found
            .Select(x => new MorphParse(x.FixedWord, x.Tag, x.NormalForm,
                (double)x.Count / totalCounts[x.PrefixId] * KnownSuffixMultiplier, x.ParaKey, x.Stack))
            .OrderByDescending(p => p.Score)
            .ToList();
    }

    // ---------- Неизвестное слово ----------

    private List<MorphParse> UnknownParse(string word, string wordLower, HashSet<string> seen) =>
        seen.Count > 0
            ? new()
            : new() { new MorphParse(wordLower, UnknownTag, wordLower, 1.0, null, $"[UNKN|{word}]") };
}
