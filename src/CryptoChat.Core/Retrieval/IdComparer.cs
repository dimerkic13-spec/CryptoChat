namespace CryptoChat.Core.Retrieval;

/// <summary>Сравнивает id вида 1.1.1.10 по числам, а не по буквам: 1.1.1.2 идёт раньше 1.1.1.10.</summary>
public sealed class IdComparer : IComparer<string>
{
    public static readonly IdComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var a = x.Split('.');
        var b = y.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            int result = int.TryParse(a[i], out var left) && int.TryParse(b[i], out var right)
                ? left.CompareTo(right)
                : string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }
}
