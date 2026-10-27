using CryptoChat.Core.Models;
using CryptoChat.Core.Retrieval;

namespace CryptoChat.Core.Dialog;

public enum DecisionKind
{
    NotUnderstood,
    Escalate,
    Answer,
    Clarify,
    ClarifyBranch,
    ClarifyProduct
}

public sealed record DialogDecision(
    DecisionKind Kind,
    IReadOnlyList<Candidate> Candidates,
    IReadOnlyList<Category> Products);

public interface IDialogPolicy
{
    DialogDecision Decide(RetrievalResult result);
}
