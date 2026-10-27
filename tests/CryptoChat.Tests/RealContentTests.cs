using CryptoChat.Core.Knowledge;
using Xunit;

namespace CryptoChat.Tests;

/// <summary>Проверяет настоящий каталог content/ из репозитория.</summary>
public class RealContentTests
{
    private static string FindContentDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "taxonomy.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Каталог content/ не найден выше папки с тестами.");
    }

    [Fact]
    public void RealContent_HasNoValidationErrors()
    {
        var content = KnowledgeLoader.LoadContent(FindContentDir());
        var errors = ContentValidator.Validate(content).Where(i => i.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void RealContent_LoadsExpectedNumberOfQuestions()
    {
        var kb = KnowledgeLoader.Load(FindContentDir());
        Assert.Equal(38, kb.Questions.Count);
        Assert.Equal(15, kb.QuestionsUnder("1.1").Count());
        Assert.Equal(17, kb.QuestionsUnder("1.2").Count());
        Assert.Equal(6, kb.QuestionsUnder("1.3").Count());
        Assert.Empty(kb.QuestionsUnder("2"));
    }

    [Fact]
    public void RealContent_QuestionTitlesAreUnique()
    {
        var kb = KnowledgeLoader.Load(FindContentDir());
        var duplicates = kb.Questions.GroupBy(q => q.Title).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }
}
