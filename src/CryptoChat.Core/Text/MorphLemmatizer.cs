namespace CryptoChat.Core.Text;

/// <summary>Начальная форма слова по словарям pymorphy3 («рутокена» → «рутокен»).</summary>
public sealed class MorphLemmatizer : ILemmatizer
{
    public string Name => "pymorphy3";

    public string Lemmatize(string word) => word;

    public static MorphLemmatizer Load() =>
        throw new MorphologyUnavailableException("Морфология пока не подключена.");
}
