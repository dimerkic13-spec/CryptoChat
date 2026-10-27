namespace CryptoChat.Core.Text;

/// <summary>Приводит текст к единому виду: нижний регистр, «ё» → «е».</summary>
public static class Normalizer
{
    public static string Normalize(string text) =>
        text.ToLowerInvariant().Replace('ё', 'е');
}
