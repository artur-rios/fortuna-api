using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArturRios.Fortuna.Data.Configuration;

/// <summary>
///     Replaces the <c>EF_DECIMAL</c> collation EF Core registers on every SQLite connection with one that reads
///     the values in the invariant culture.
/// </summary>
/// <remarks>
///     SQLite stores decimals as TEXT, written in the invariant culture ("9.99"), and EF Core orders a decimal
///     column with <c>COLLATE EF_DECIMAL</c>. EF Core's own collation parses both values with the process's
///     culture, so under pt-BR, where '.' groups thousands, "9.99" reads as 999 and "100.5" as 1005, and an
///     ordering by amount comes back in the wrong order. The <c>ef_*</c> functions are not affected: they receive
///     decimals Microsoft.Data.Sqlite has already read in the invariant culture. The stored values do not change.
/// </remarks>
internal sealed class SqliteDecimalCollation : DbConnectionInterceptor
{
    public const string Name = "EF_DECIMAL";

    public static SqliteDecimalCollation Instance { get; } = new();

    private SqliteDecimalCollation()
    {
    }

    /// <summary>Compares two decimals stored as invariant-culture TEXT, the way SQLite stores them.</summary>
    public static int Compare(string left, string right) => decimal.Compare(Parse(left), Parse(right));

    // EF Core registers its collation while it creates the connection, before this runs; registering the same
    // name again replaces it, so every later open (and any open outside EF Core) uses this one.
    public override DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, DbConnection result)
    {
        if (result is SqliteConnection connection)
        {
            connection.CreateCollation(Name, Compare);
        }

        return result;
    }

    // The same styles Microsoft.Data.Sqlite reads a decimal column with.
    private static decimal Parse(string value) =>
        decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
}
