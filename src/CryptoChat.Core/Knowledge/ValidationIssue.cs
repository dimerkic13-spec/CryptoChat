namespace CryptoChat.Core.Knowledge;

public enum Severity
{
    Error,
    Warning
}

public sealed record ValidationIssue(string Code, Severity Severity, string Where, string Message)
{
    public override string ToString() =>
        $"{(Severity == Severity.Error ? "ОШИБКА" : "предупреждение")} {Code} [{Where}]: {Message}";
}
