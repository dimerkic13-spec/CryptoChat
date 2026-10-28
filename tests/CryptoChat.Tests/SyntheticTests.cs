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
    [InlineData("pymorphy3", 0.85)] // 0.2.5a: 0.755, 0.2.6a: 0.852, 0.2.7a: 0.868
    [InlineData("stemmer", 0.85)]   // 0.2.5a: 0.756, 0.2.6a: 0.843, 0.2.7a: 0.865
    public void RightQuestionComesFirst(string morphology, double minimum)
    {
        var (share, wrong, total) = Measure(morphology);
        Assert.True(share >= minimum, $"верный вопрос первым: {share:P1} из {total}, нужно не меньше {minimum:P0}");
        Assert.True(wrong <= 1, $"неверных прямых ответов: {wrong}");
    }

    /// <summary>Пользователь выбирает подходящий пункт меню: верный ответ не больше чем за 3 уточнения.</summary>
    [Theory]
    [InlineData("pymorphy3", 0.97)] // 0.2.6a: 0.932, 0.2.7a: 0.979
    [InlineData("stemmer", 0.96)]   // 0.2.6a: 0.931, 0.2.7a: 0.974
    public void DialogReachesAnswerWithin3Steps(string morphology, double minimum)
    {
        var engine = ChatEngine.Create(ContentDir, morphology, semantic: false);
        var results = Synthetic.EvaluateDialogs(engine, Synthetic.Load(ContentDir));
        var within3 = (double)results.Count(r => r.Outcome == Synthetic.DialogOutcome.Answer && r.Steps <= 3) / results.Count;
        var wrong = results.Count(r => r.Outcome == Synthetic.DialogOutcome.Wrong);
        Assert.True(within3 >= minimum, $"верный ответ за ≤3 уточнения: {within3:P1}");
        Assert.True(wrong <= 1, $"неверных ответов: {wrong}");
    }
}
