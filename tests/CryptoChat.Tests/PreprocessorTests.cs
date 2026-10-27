using CryptoChat.Core.Text;
using Xunit;

namespace CryptoChat.Tests;

public class PreprocessorTests
{
    private static string Run(string text, SynonymMap? synonyms = null) =>
        string.Join(" ", new TextProcessor(synonyms: synonyms).Lemmatize(text));

    [Theory]
    [InlineData("как купить лицензию", "куп лицензи")]
    [InlineData("какие порты ipsec", "порт ipsec")]
    [InlineData("что такое кластер ngate", "кластер ngate")]
    [InlineData("я не вижу счет в эдо", "счет эд")]
    [InlineData("Пин-код рутокен", "пин код рутокен")]
    [InlineData("ЛИЦЕНЗИЯ истекла", "лицензи истекл")]
    [InlineData("версия 5.0.", "верси 5.0")]
    [InlineData("счёт", "счет")]
    [InlineData("как и что", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Lemmatize_ReturnsExpectedLemmas(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("как вернуть деньги за лицензию", "верн лицензи")]
    [InlineData("возврат лицензии", "верн лицензи")]
    public void Lemmatize_AppliesSynonyms(string text, string expected)
    {
        var synonyms = new SynonymMap(new[]
        {
            new[] { "вернуть", "возврат", "вернуть деньги" }
        });
        Assert.Equal(expected, Run(text, synonyms));
    }

    [Fact]
    public void SynonymMap_GroupWithOneWord_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new SynonymMap(new[] { new[] { "вернуть" } }));
    }

    [Fact]
    public void SynonymMap_WordInTwoGroups_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SynonymMap(new[]
            {
                new[] { "вернуть", "возврат" },
                new[] { "отдать", "возврат" }
            }));
    }
}
