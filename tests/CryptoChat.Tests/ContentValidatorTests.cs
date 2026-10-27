using CryptoChat.Core.Knowledge;
using Xunit;

namespace CryptoChat.Tests;

public class ContentValidatorTests
{
    private static IReadOnlyList<ValidationIssue> Check(ContentSet content) => ContentValidator.Validate(content);

    [Fact]
    public void ValidContent_HasNoIssues()
    {
        Assert.Empty(Check(TestContent.Valid()));
    }

    [Fact]
    public void V001_DuplicateQuestionId()
    {
        var c = TestContent.Valid();
        c.Questions[1].Id = "1.1.1";
        Assert.Contains(Check(c), i => i.Code == "V001");
    }

    [Fact]
    public void V001_DuplicateCategoryId()
    {
        var c = TestContent.Valid();
        c.Taxonomy.Categories[0].Children.Add(new RawCategory { Id = "1.1", Name = "Копия", Kind = "subtopic" });
        Assert.Contains(Check(c), i => i.Code == "V001");
    }

    [Fact]
    public void V002_UnknownCategory()
    {
        var c = TestContent.Valid();
        c.Questions[0].Category = "9.9";
        Assert.Contains(Check(c), i => i.Code == "V002");
    }

    [Fact]
    public void V002_CategoryIsNotLeaf()
    {
        var c = TestContent.Valid();
        c.Questions[0].Category = "1";
        c.Questions[0].Id = "1.5";
        Assert.Contains(Check(c), i => i.Code == "V002");
    }

    [Fact]
    public void V003_QuestionIdDoesNotMatchCategory()
    {
        var c = TestContent.Valid();
        c.Questions[0].Id = "9.9.9";
        Assert.Contains(Check(c), i => i.Code == "V003");
    }

    [Fact]
    public void V004_EmptyAnswer()
    {
        var c = TestContent.Valid();
        c.Questions[0].Answer = "   ";
        Assert.Contains(Check(c), i => i.Code == "V004");
    }

    [Fact]
    public void V005_SourceIsNotHttps()
    {
        var c = TestContent.Valid();
        c.Questions[0].Source!.Url = "http://example.com";
        Assert.Contains(Check(c), i => i.Code == "V005");
    }

    [Fact]
    public void V005_SourceIsMissing()
    {
        var c = TestContent.Valid();
        c.Questions[0].Source = null;
        Assert.Contains(Check(c), i => i.Code == "V005");
    }

    [Fact]
    public void V006_TooFewKeywords()
    {
        var c = TestContent.Valid();
        c.Questions[0].Keywords.RemoveAt(2);
        Assert.Contains(Check(c), i => i.Code == "V006");
    }

    [Fact]
    public void V007_WeightOutOfRange()
    {
        var c = TestContent.Valid();
        c.Questions[0].Keywords[0].Weight = 0;
        Assert.Contains(Check(c), i => i.Code == "V007");
    }

    [Fact]
    public void V008_DuplicateKeywordAfterStemming()
    {
        var c = TestContent.Valid();
        c.Questions[0].Keywords[2].Word = "лицензию"; // та же основа, что у «лицензия»
        Assert.Contains(Check(c), i => i.Code == "V008");
    }

    [Fact]
    public void V009_MixedLatinAndCyrillic()
    {
        var c = TestContent.Valid();
        c.Questions[1].Keywords[2].Word = "ngаte"; // «а» здесь кириллическая
        Assert.Contains(Check(c), i => i.Code == "V009");
    }

    [Fact]
    public void V010_KeywordEmptyAfterStemming()
    {
        var c = TestContent.Valid();
        c.Questions[0].Keywords[2].Word = "и";
        Assert.Contains(Check(c), i => i.Code == "V010");
    }

    [Fact]
    public void V011_ChildIdDoesNotStartWithParentId()
    {
        var c = TestContent.Valid();
        c.Taxonomy.Categories[0].Children[0].Id = "3.1";
        Assert.Contains(Check(c), i => i.Code == "V011");
    }

    [Fact]
    public void V012_GroupWithOneWord()
    {
        var c = TestContent.Valid();
        c.Synonyms.Groups.Add(new List<string> { "пин" });
        Assert.Contains(Check(c), i => i.Code == "V012");
    }

    [Fact]
    public void V012_WordInTwoGroups()
    {
        var c = TestContent.Valid();
        c.Synonyms.Groups.Add(new List<string> { "отдать", "возврат" });
        Assert.Contains(Check(c), i => i.Code == "V012");
    }

    [Fact]
    public void V013_EmptyLeafCategory_IsOnlyWarning()
    {
        var c = TestContent.Valid();
        c.Taxonomy.Categories[0].Children.Add(new RawCategory { Id = "1.2", Name = "Документы", Kind = "subtopic" });
        var issues = Check(c);
        Assert.Contains(issues, i => i.Code == "V013" && i.Severity == Severity.Warning);
        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
    }

    [Fact]
    public void V014_KeywordIsStopWord_IsOnlyWarning()
    {
        var c = TestContent.Valid();
        c.Questions[0].Keywords[2].Word = "как";
        var issues = Check(c);
        Assert.Contains(issues, i => i.Code == "V014" && i.Severity == Severity.Warning);
        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
    }

    [Fact]
    public void V016_BadRevisionFormat_IsOnlyWarning()
    {
        var c = TestContent.Valid();
        c.Questions[0].Source!.Revision = "09.10.2026";
        var issues = Check(c);
        Assert.Contains(issues, i => i.Code == "V016" && i.Severity == Severity.Warning);
        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
    }
}
