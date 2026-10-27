using System.Globalization;
using System.Text;
using CryptoChat.Core.Models;

namespace CryptoChat.Core.Answer;

public interface IAnswerComposer
{
    string Compose(Question question, double score, Category? section);
}

/// <summary>Готовый ответ из базы, ссылка на источник и служебная строка.</summary>
public sealed class TemplateComposer : IAnswerComposer
{
    public string Compose(Question question, double score, Category? section)
    {
        var url = string.IsNullOrEmpty(question.Anchor)
            ? question.SourceUrl
            : $"{question.SourceUrl}#{question.Anchor}";
        var revision = string.IsNullOrEmpty(question.Revision) ? "" : $" (ред. {question.Revision})";
        var sectionPart = section == null ? "" : $", раздел: {section.Id} {section.Name}";

        var sb = new StringBuilder();
        sb.Append(question.Answer).Append('\n');
        sb.Append("📎 Источник: ").Append(url).Append(revision).Append('\n');
        sb.Append("(score: ").Append(score.ToString("0.00", CultureInfo.InvariantCulture)).Append(sectionPart).Append(')');
        return sb.ToString();
    }
}
