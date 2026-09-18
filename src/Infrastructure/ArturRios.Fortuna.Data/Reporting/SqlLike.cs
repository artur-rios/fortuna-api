namespace ArturRios.Fortuna.Data.Reporting;

/// <summary>
/// Makes user text match literally inside a LIKE pattern: the escape character, <c>%</c> and
/// <c>_</c> are prefixed with a backslash, so the pattern must declare a backslash escape.
/// </summary>
internal static class SqlLike
{
    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    public static string Contains(string value) => $"%{Escape(value)}%";
}
