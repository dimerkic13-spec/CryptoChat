using CryptoChat.Core.Text;
using Xunit;

namespace CryptoChat.Tests;

public class StemmerTests
{
    [Theory]
    [InlineData("вернуть", "верн")]
    [InlineData("лицензию", "лицензи")]
    [InlineData("истекал", "истек")]
    [InlineData("счета", "счет")]
    [InlineData("договор", "договор")]
    [InlineData("ngate", "ngate")]
    [InlineData("ipsec", "ipsec")]
    [InlineData("пин", "пин")]
    [InlineData("эдо", "эд")]
    public void Stem_ReturnsExpectedBase(string word, string expected)
    {
        Assert.Equal(expected, Stemmer.Stem(word));
    }

    [Fact]
    public void Stem_IsCaseInsensitive()
    {
        Assert.Equal("лицензи", Stemmer.Stem("ЛИЦЕНЗИЮ"));
    }
}
