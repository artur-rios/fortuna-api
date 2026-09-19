using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace ArturRios.Fortuna.Data.Configuration;

internal static class DatabaseException
{
    private const int SqliteConstraintError = 19;
    private const int SqlitePrimaryKeyViolation = 1555;
    private const int SqliteUniqueViolation = 2067;
    private const string SqliteUniqueFailurePrefix = "UNIQUE constraint failed: ";

    public static bool IsUniqueViolation(
        DbUpdateException exception,
        params string[] constraintNames)
    {
        if (exception.InnerException is SqliteException
            {
                SqliteErrorCode: SqliteConstraintError,
                SqliteExtendedErrorCode: SqlitePrimaryKeyViolation or SqliteUniqueViolation
            } sqlite)
        {
            return constraintNames.Length == 0 ||
                   SqliteViolationMatches(exception, sqlite, constraintNames);
        }

        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres &&
            (constraintNames.Length == 0 ||
             constraintNames.Contains(postgres.ConstraintName, StringComparer.Ordinal));
    }

    // SQLite reports the violated columns ("UNIQUE constraint failed: table.a, table.b")
    // rather than the index name, so the named indexes are resolved to their columns
    // through the model of the context that attempted the save.
    private static bool SqliteViolationMatches(
        DbUpdateException exception,
        SqliteException sqlite,
        IReadOnlyCollection<string> constraintNames)
    {
        var failedColumns = FailedColumns(sqlite.Message);
        var model = exception.Entries.FirstOrDefault()?.Context.Model;
        if (failedColumns is null || model is null)
        {
            return true;
        }

        var constraints = UniqueConstraintColumns(model)
            .Where(constraint => constraintNames.Contains(constraint.Name, StringComparer.Ordinal))
            .ToArray();
        if (constraints.Length == 0)
        {
            return true;
        }

        return constraints.Any(constraint => constraint.Columns.SequenceEqual(
            failedColumns,
            StringComparer.Ordinal));
    }

    private static string[]? FailedColumns(string message)
    {
        var start = message.IndexOf(SqliteUniqueFailurePrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += SqliteUniqueFailurePrefix.Length;
        var end = message.IndexOf('\'', start);
        var columns = end < 0 ? message[start..] : message[start..end];

        return columns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static IEnumerable<(string Name, string[] Columns)> UniqueConstraintColumns(IModel model)
    {
        foreach (var table in model.GetRelationalModel().Tables)
        {
            foreach (var index in table.Indexes.Where(index => index.IsUnique))
            {
                yield return (index.Name, Columns(table, index.Columns));
            }

            foreach (var constraint in table.UniqueConstraints)
            {
                yield return (constraint.Name, Columns(table, constraint.Columns));
            }
        }
    }

    private static string[] Columns(ITable table, IEnumerable<IColumn> columns) => columns
        .Select(column => $"{table.Name}.{column.Name}")
        .ToArray();
}
