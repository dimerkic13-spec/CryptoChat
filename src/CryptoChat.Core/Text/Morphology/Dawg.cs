using System.Buffers.Binary;
using System.Text;

namespace CryptoChat.Core.Text.Morphology;

// Чтение словарей в формате DAWG (dawgdic) — перенос библиотеки DAWG-Python (MIT),
// на которой работает pymorphy3. Словарь — массив 32-битных «узлов»; переход по байту
// ключа вычисляется из смещения узла. Тексты ключей хранятся в UTF-8.

/// <summary>Узлы словаря и переходы по ним.</summary>
internal sealed class DawgDictionary
{
    private const uint PrecisionMask = 0xFFFFFFFF;
    private const uint IsLeafBit = 1u << 31;
    private const uint HasLeafBit = 1u << 8;
    private const uint ExtensionBit = 1u << 9;

    public const int Root = 0;

    /// <summary>Перехода нет.</summary>
    public const int None = -1;

    private uint[] _units = Array.Empty<uint>();

    public static DawgDictionary Read(Stream stream)
    {
        var size = ReadUInt32(stream);
        var bytes = ReadExactly(stream, checked((int)size * 4));
        var units = new uint[size];
        for (int i = 0; i < units.Length; i++)
            units[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        return new DawgDictionary { _units = units };
    }

    private static uint Offset(uint unit) => ((unit >> 10) << (int)((unit & ExtensionBit) >> 6)) & PrecisionMask;
    private static uint Label(uint unit) => unit & (IsLeafBit | 0xFF);

    public bool HasValue(int index) => (_units[index] & HasLeafBit) != 0;

    public int Value(int index)
    {
        var valueIndex = (int)(((uint)index ^ Offset(_units[index])) & PrecisionMask);
        return (int)(_units[valueIndex] & ~IsLeafBit);
    }

    /// <summary>Переход по одному байту. None, если перехода нет.</summary>
    public int FollowChar(byte label, int index)
    {
        var next = ((uint)index ^ Offset(_units[index]) ^ label) & PrecisionMask;
        if (next >= _units.Length || Label(_units[next]) != label)
            return None;
        return (int)next;
    }

    /// <summary>Переход по нескольким байтам. None, если перехода нет.</summary>
    public int FollowBytes(ReadOnlySpan<byte> bytes, int index)
    {
        foreach (var b in bytes)
        {
            index = FollowChar(b, index);
            if (index == None)
                return None;
        }
        return index;
    }

    /// <summary>Значение по точному ключу или -1.</summary>
    public int Find(ReadOnlySpan<byte> key)
    {
        var index = FollowBytes(key, Root);
        if (index == None || !HasValue(index))
            return -1;
        return Value(index);
    }

    internal static uint ReadUInt32(Stream stream) =>
        BinaryPrimitives.ReadUInt32LittleEndian(ReadExactly(stream, 4));

    internal static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }
}

/// <summary>Подсказки для перебора ключей: первый ребёнок и следующий брат каждого узла.</summary>
internal sealed class DawgGuide
{
    private byte[] _units = Array.Empty<byte>();

    public static DawgGuide Read(Stream stream)
    {
        var size = DawgDictionary.ReadUInt32(stream);
        return new DawgGuide { _units = DawgDictionary.ReadExactly(stream, checked((int)size * 2)) };
    }

    public byte Child(int index) => _units[index * 2];
    public byte Sibling(int index) => _units[index * 2 + 1];
    public int Size => _units.Length;
}

/// <summary>Перебор всех ключей, которые продолжают заданный узел.</summary>
internal sealed class DawgCompleter
{
    private readonly DawgDictionary _dic;
    private readonly DawgGuide _guide;
    private readonly List<int> _indexStack = new();
    private int _lastIndex;

    public List<byte> Key { get; } = new();

    public DawgCompleter(DawgDictionary dic, DawgGuide guide)
    {
        _dic = dic;
        _guide = guide;
    }

    public int Value() => _dic.Value(_lastIndex);

    public void Start(int index, ReadOnlySpan<byte> prefix = default)
    {
        Key.Clear();
        Key.AddRange(prefix.ToArray());
        _indexStack.Clear();
        if (_guide.Size > 0)
        {
            _indexStack.Add(index);
            _lastIndex = DawgDictionary.Root;
        }
    }

    public bool Next()
    {
        if (_indexStack.Count == 0)
            return false;

        var index = _indexStack[^1];

        if (_lastIndex != DawgDictionary.Root)
        {
            var childLabel = _guide.Child(index);
            if (childLabel != 0)
            {
                // Переход к первому ребёнку.
                index = Follow(childLabel, index);
                if (index == DawgDictionary.None)
                    return false;
            }
            else
            {
                while (true)
                {
                    var siblingLabel = _guide.Sibling(index);
                    // Возврат к предыдущему узлу.
                    if (Key.Count > 0)
                        Key.RemoveAt(Key.Count - 1);

                    _indexStack.RemoveAt(_indexStack.Count - 1);
                    if (_indexStack.Count == 0)
                        return false;

                    index = _indexStack[^1];
                    if (siblingLabel != 0)
                    {
                        // Переход к следующему брату.
                        index = Follow(siblingLabel, index);
                        if (index == DawgDictionary.None)
                            return false;
                        break;
                    }
                }
            }
        }

        return FindTerminal(index);
    }

    private int Follow(byte label, int index)
    {
        var next = _dic.FollowChar(label, index);
        if (next == DawgDictionary.None)
            return DawgDictionary.None;
        Key.Add(label);
        _indexStack.Add(next);
        return next;
    }

    private bool FindTerminal(int index)
    {
        while (!_dic.HasValue(index))
        {
            var label = _guide.Child(index);
            index = _dic.FollowChar(label, index);
            if (index == DawgDictionary.None)
                return false;
            Key.Add(label);
            _indexStack.Add(index);
        }
        _lastIndex = index;
        return true;
    }
}

/// <summary>
/// Словарь «слово → несколько записей». Записи хранятся в самих ключах: «слово\x01base64(запись)».
/// Каждая запись — несколько беззнаковых целых в порядке big-endian (формат struct ">HH" или ">IHH").
/// </summary>
internal sealed class RecordDawg
{
    private const byte PayloadSeparator = 0x01;

    private readonly DawgDictionary _dic;
    private readonly DawgGuide _guide;
    private readonly int[] _fieldSizes;

    private RecordDawg(DawgDictionary dic, DawgGuide guide, int[] fieldSizes)
    {
        _dic = dic;
        _guide = guide;
        _fieldSizes = fieldSizes;
    }

    /// <param name="fieldSizes">Размеры полей записи в байтах: {2, 2} для ">HH", {4, 2, 2} для ">IHH".</param>
    public static RecordDawg Load(string path, params int[] fieldSizes)
    {
        using var stream = File.OpenRead(path);
        var dic = DawgDictionary.Read(stream);
        var guide = DawgGuide.Read(stream);
        return new RecordDawg(dic, guide, fieldSizes);
    }

    private int[] Decode(List<byte> base64)
    {
        var bytes = Convert.FromBase64String(Encoding.ASCII.GetString(base64.ToArray()));
        var result = new int[_fieldSizes.Length];
        var position = 0;
        for (int i = 0; i < _fieldSizes.Length; i++)
        {
            result[i] = _fieldSizes[i] == 4
                ? (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position))
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position));
            position += _fieldSizes[i];
        }
        return result;
    }

    private List<int[]> ValueForIndex(int index)
    {
        var result = new List<int[]>();
        var completer = new DawgCompleter(_dic, _guide);
        completer.Start(index);
        while (completer.Next())
            result.Add(Decode(completer.Key));
        return result;
    }

    /// <summary>
    /// Все варианты ключа с учётом замен букв (например, «е» → «ё») и их записи.
    /// Порядок как в DAWG-Python: сначала ключ без замен, затем варианты с заменами.
    /// </summary>
    public List<(string Key, List<int[]> Values)> SimilarItems(string key, IReadOnlyDictionary<int, int[]> replaces)
    {
        var codePoints = key.EnumerateRunes().Select(r => r.Value).ToArray();
        return SimilarItems(Array.Empty<int>(), codePoints, DawgDictionary.Root, replaces);
    }

    private List<(string Key, List<int[]> Values)> SimilarItems(int[] currentPrefix, int[] key, int index,
        IReadOnlyDictionary<int, int[]> replaces)
    {
        var result = new List<(string, List<int[]>)>();
        var startPos = currentPrefix.Length;
        var wordPos = startPos;
        Span<byte> buffer = stackalloc byte[4];

        while (wordPos < key.Length)
        {
            if (replaces.TryGetValue(key[wordPos], out var replacements))
            {
                foreach (var replacement in replacements)
                {
                    var next = _dic.FollowBytes(Utf8(replacement, buffer), index);
                    if (next > 0)
                    {
                        var prefix = currentPrefix.Concat(key[startPos..wordPos]).Append(replacement).ToArray();
                        result.AddRange(SimilarItems(prefix, key, next, replaces));
                    }
                }
            }

            index = _dic.FollowBytes(Utf8(key[wordPos], buffer), index);
            if (index <= 0)
                return result;
            wordPos++;
        }

        index = _dic.FollowChar(PayloadSeparator, index);
        if (index > 0)
        {
            var found = currentPrefix.Concat(key[startPos..]).ToArray();
            result.Insert(0, (FromCodePoints(found), ValueForIndex(index)));
        }
        return result;
    }

    private static ReadOnlySpan<byte> Utf8(int codePoint, Span<byte> buffer)
    {
        var written = new Rune(codePoint).EncodeToUtf8(buffer);
        return buffer[..written];
    }

    private static string FromCodePoints(int[] codePoints)
    {
        var sb = new StringBuilder(codePoints.Length);
        foreach (var cp in codePoints)
            sb.Append(new Rune(cp).ToString());
        return sb.ToString();
    }
}

/// <summary>Словарь «строка → целое число» (формат IntCompletionDAWG).</summary>
internal sealed class IntDawg
{
    private readonly DawgDictionary _dic;

    private IntDawg(DawgDictionary dic) => _dic = dic;

    public static IntDawg Load(string path)
    {
        using var stream = File.OpenRead(path);
        return new IntDawg(DawgDictionary.Read(stream)); // подсказки для перебора не нужны
    }

    /// <summary>Значение по ключу или defaultValue.</summary>
    public int Get(string key, int defaultValue = 0)
    {
        var value = _dic.Find(Encoding.UTF8.GetBytes(key));
        return value == -1 ? defaultValue : value;
    }
}
