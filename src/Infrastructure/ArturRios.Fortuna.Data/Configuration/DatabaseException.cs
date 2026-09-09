using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Configuration;

internal static class DatabaseException
{
    public static bool IsUniqueViolation(
        DbUpdateException exception,
        params string[] postgresConstraintNames)
    {
        if (exception.InnerException is SqliteException
            {
                SqliteErrorCode: 19,
                SqliteExtendedErrorCode: 1555 or 2067
            })
        {
            return true;
        }

        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres &&
            (postgresConstraintNames.Length == 0 ||
             postgresConstraintNames.Contains(postgres.ConstraintName, StringComparer.Ordinal));
    }
}
