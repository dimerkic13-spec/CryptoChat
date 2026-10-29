using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CryptoChat.Core.Semantic;

/// <summary>Описание выгруженной модели (файл cryptochat-model.json, его пишет tools/export_onnx.py).</summary>
public sealed class ModelInfo
{
    public const string FileName = "cryptochat-model.json";

    public int Format { get; set; }
    public string Model { get; set; } = "";
    public int MaxSeqLength { get; set; } = 512;
    public bool DoLowerCase { get; set; }
    public int Dimension { get; set; }
    public string Output { get; set; } = "sentence_embedding";
    public List<ReferenceEmbedding> Reference { get; set; } = new();

    /// <summary>Эталон из Python: текст, его токены и вектор sentence-transformers.</summary>
    public sealed class ReferenceEmbedding
    {
        public string Text { get; set; } = "";
        public List<int> Ids { get; set; } = new();
        public List<double> Vector { get; set; } = new();
    }

    public static ModelInfo Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        return JsonSerializer.Deserialize<ModelInfo>(File.ReadAllText(path, Encoding.UTF8),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException($"Файл пуст: {path}");
    }
}

/// <summary>
/// Модель sentence-transformers, выгруженная в формат ONNX (вместе с усреднением и нормировкой вектора),
/// запускается через ONNX Runtime на процессоре. Видеокарта и Python не нужны.
/// </summary>
public sealed class OnnxEncoder : IEncoder, IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly bool _hasTokenTypes;

    public string Name { get; }
    public ModelInfo Info { get; }
    public string Folder { get; }

    private OnnxEncoder(InferenceSession session, WordPieceTokenizer tokenizer, ModelInfo info, string folder)
    {
        _session = session;
        _tokenizer = tokenizer;
        Info = info;
        Folder = folder;
        Name = info.Model;
        _hasTokenTypes = session.InputMetadata.ContainsKey("token_type_ids");
    }

    /// <summary>Папка модели внутри cacheDir: models/onnx/cointegrated--rubert-tiny2.</summary>
    public static string ModelFolder(string cacheDir, string model) =>
        Path.Combine(cacheDir, "onnx", model.Replace("/", "--"));

    public static OnnxEncoder Load(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "model.onnx")))
            throw new SemanticUnavailableException(
                $"Модель не найдена в {folder}. Выгрузите её из Python-версии скриптом tools/export_onnx.py " +
                "(см. README, раздел «Поиск по смыслу»).");
        try
        {
            var info = ModelInfo.Load(folder);
            var tokenizer = WordPieceTokenizer.Load(Path.Combine(folder, "tokenizer.json"));
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            var session = new InferenceSession(Path.Combine(folder, "model.onnx"), options);
            return new OnnxEncoder(session, tokenizer, info, folder);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException
                                       or OnnxRuntimeException or KeyNotFoundException or InvalidOperationException)
        {
            throw new SemanticUnavailableException($"Не удалось загрузить модель из {folder}: {ex.Message}", ex);
        }
    }

    /// <summary>Номера токенов текста (так же, как в sentence-transformers: пробелы по краям убираются).</summary>
    public IReadOnlyList<int> Tokenize(string text)
    {
        text = text.Trim();
        if (Info.DoLowerCase)
            text = text.ToLowerInvariant();
        return _tokenizer.Encode(text, Info.MaxSeqLength);
    }

    public IReadOnlyList<double[]> Encode(IReadOnlyList<string> texts) =>
        texts.Select(EncodeOne).ToList();

    private double[] EncodeOne(string text)
    {
        var ids = Tokenize(text);
        var shape = new[] { 1, ids.Count };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids.Select(i => (long)i).ToArray(), shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, ids.Count).ToArray(), shape)),
        };
        if (_hasTokenTypes)
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Count], shape)));

        using var results = _session.Run(inputs, new[] { Info.Output });
        return results[0].AsEnumerable<float>().Select(x => (double)x).ToArray();
    }

    public void Dispose() => _session.Dispose();
}
