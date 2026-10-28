using CryptoChat.Cli;
using CryptoChat.Core;

namespace CryptoChat.Tests;

/// <summary>
/// Качество на content/synthetic.json (1900 запросов, по 50 на вопрос). Пороги — чуть ниже достигнутого в 0.2.6a:
/// если тест упал после правки базы, посмотрите таблицу по вопросам (ключ --synthetic) и верните качество.
/// </summary>
public class SyntheticQualityTests
{
    private static readonly string ContentDir = TestSupport.FindContentDir();

    private static (double FirstShare, int Wrong, int Total) Measure(string morphology)
    {
        var engine = ChatEngine.Create(ContentDir, morphology, semantic: false);
        var results = Synthetic.Evaluate(engine, Synthetic.Load(ContentDir));
        var first = results.Count(r => r.Outcome is Synthetic.Outcome.Answer or Synthetic.Outcome.First);
        var wrong = results.Count(r => r.Outcome == Synthetic.Outcome.WrongAnswer);
        return ((double)first / results.Count, wrong, results.Count);
    }

    [Fact]
    public void FileHas50QueriesForEveryQuestion()
    {
        var cases = Synthetic.Load(ContentDir);
        var ids = ChatEngine.Create(ContentDir, "stemmer", semantic: false).KnowledgeBase.Questions.Select(q => q.Id);
        Assert.Equal(ids.OrderBy(x => x), cases.Keys.OrderBy(x => x));
        Assert.All(cases.Values, v => Assert.Equal(50, v.Distinct().Count()));
    }

    [Theory]
    [InlineData("pymorphy3", 0.84)] // 0.2.5a: 0.755, 0.2.6a: 0.852
    [InlineData("stemmer", 0.83)]   // 0.2.5a: 0.756, 0.2.6a: 0.843
    public void RightQuestionComesFirst(string morphology, double minimum)
    {
        var (share, wrong, total) = Measure(morphology);
        Assert.True(share >= minimum, $"верный вопрос первым: {share:P1} из {total}, нужно не меньше {minimum:P0}");
        Assert.True(wrong <= 1, $"неверных прямых ответов: {wrong}");
    }
}
