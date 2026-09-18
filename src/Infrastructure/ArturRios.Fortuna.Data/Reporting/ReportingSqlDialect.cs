using System.Data.Common;
using System.Diagnostics;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Shared.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArturRios.Fortuna.Data.Reporting;

/// <summary>
/// The provider-specific pieces of the hand-written reporting SQL. Queries are written once, with
/// schema-qualified table names, and every expression whose syntax or semantics differ between
/// PostgreSQL and SQLite goes through a member of this class.
/// </summary>
/// <remarks>
/// SQLite stores decimals as TEXT and timestamps as UTC ticks (see <see cref="AppDbContext"/>),
/// so decimal arithmetic, comparison and summing use the <c>ef_*</c> functions EF Core registers
/// on every SQLite connection it creates, which keep exact decimal semantics.
/// </remarks>
internal abstract class ReportingSqlDialect
{
    public static readonly ReportingSqlDialect PostgreSql = new PostgreSqlDialect();
    public static readonly ReportingSqlDialect Sqlite = new SqliteDialect();

    public static ReportingSqlDialect For(DatabaseFacade database) =>
        database.IsSqlite() ? Sqlite : PostgreSql;

    /// <summary>Adapts SQL written with <c>fortuna.</c>-qualified tables to the provider.</summary>
    public abstract string QualifyTables(string sql);

    /// <summary>A uuid column rendered as its canonical lowercase text.</summary>
    public abstract string UuidText(string expression);

    public abstract string NullText { get; }

    public abstract string NullDate { get; }

    /// <summary>A parameter reference usable in <c>@parameter IS NULL</c> checks.</summary>
    public abstract string TypedParameter(string name, string postgreSqlType);

    /// <summary>The first day of the period that contains <paramref name="date"/>, as a date.</summary>
    public abstract string PeriodStart(string date, AggregationGranularity granularity);

    /// <summary>A date expression rendered as <c>yyyy-MM-dd</c> text.</summary>
    public abstract string DateText(string date);

    public abstract string Negate(string decimalExpression);

    public abstract string Sum(string decimalExpression);

    public abstract string Count { get; }

    /// <summary>A decimal comparison, where <paramref name="operation"/> is a SQL operator.</summary>
    public abstract string CompareDecimal(string left, string operation, string right);

    /// <summary>The expression used to order rows by a decimal column.</summary>
    public abstract string DecimalOrder(string expression);

    /// <summary>A case-insensitive LIKE whose pattern escapes with a backslash.</summary>
    public abstract string Like(string expression, string pattern);

    public abstract string IntegerTotal(string expression);

    /// <summary>Joins text values in the order of the subquery that produces them.</summary>
    public abstract string OrderedTextJoin(
        string expression,
        string separator,
        string from,
        string orderBy);

    /// <summary>Converts a typed filter value into the representation the column stores.</summary>
    public abstract object Parameter(TableColumnType type, object value);

    /// <summary>Reads a table-report cell as the CLR value the report exposes.</summary>
    public abstract Task<object?> ReadValueAsync(
        DbDataReader reader,
        int ordinal,
        TableColumnType type,
        CancellationToken cancellationToken);

    private sealed class PostgreSqlDialect : ReportingSqlDialect
    {
        public override string QualifyTables(string sql) => sql;

        public override string UuidText(string expression) => $"{expression}::text";

        public override string NullText => "NULL::text";

        public override string NullDate => "NULL::date";

        public override string TypedParameter(string name, string postgreSqlType) =>
            $"@{name}::{postgreSqlType}";

        public override string PeriodStart(string date, AggregationGranularity granularity) =>
            $"date_trunc('{Unit(granularity)}', {date}::timestamp)::date";

        public override string DateText(string date) => $"({date})::text";

        public override string Negate(string decimalExpression) => $"-{decimalExpression}";

        public override string Sum(string decimalExpression) => $"SUM({decimalExpression})";

        public override string Count => "COUNT(*)::int";

        public override string CompareDecimal(string left, string operation, string right) =>
            $"{left} {operation} {right}";

        public override string DecimalOrder(string expression) => expression;

        public override string Like(string expression, string pattern) =>
            $"{expression} ILIKE {pattern} ESCAPE E'\\\\'";

        public override string IntegerTotal(string expression) => $"({expression})::numeric";

        public override string OrderedTextJoin(
            string expression,
            string separator,
            string from,
            string orderBy) =>
            $"(SELECT COALESCE(string_agg({expression}, '{separator}' ORDER BY {orderBy}), '') " +
            $"FROM {from})";

        public override object Parameter(TableColumnType type, object value) => value;

        public override async Task<object?> ReadValueAsync(
            DbDataReader reader,
            int ordinal,
            TableColumnType type,
            CancellationToken cancellationToken) =>
            await reader.IsDBNullAsync(ordinal, cancellationToken)
                ? null
                : reader.GetValue(ordinal);

        private static string Unit(AggregationGranularity granularity) => granularity switch
        {
            AggregationGranularity.Day => "day",
            AggregationGranularity.Week => "week",
            AggregationGranularity.Month => "month",
            AggregationGranularity.Quarter => "quarter",
            AggregationGranularity.Year => "year",
            _ => throw new UnreachableException()
        };
    }

    private sealed class SqliteDialect : ReportingSqlDialect
    {
        private static readonly string SchemaPrefix = $"{AppDbContext.Schema}.";

        public override string QualifyTables(string sql) =>
            sql.Replace(SchemaPrefix, string.Empty, StringComparison.Ordinal);

        public override string UuidText(string expression) => $"lower({expression})";

        public override string NullText => "NULL";

        public override string NullDate => "NULL";

        public override string TypedParameter(string name, string postgreSqlType) => $"@{name}";

        public override string PeriodStart(string date, AggregationGranularity granularity) =>
            granularity switch
            {
                AggregationGranularity.Day => $"date({date})",
                AggregationGranularity.Week =>
                    $"date({date}, '-' || ((CAST(strftime('%w', {date}) AS INTEGER) + 6) % 7) " +
                    "|| ' days')",
                AggregationGranularity.Month => $"strftime('%Y-%m-01', {date})",
                AggregationGranularity.Quarter =>
                    $"printf('%s-%02d-01', strftime('%Y', {date}), " +
                    $"((CAST(strftime('%m', {date}) AS INTEGER) - 1) / 3) * 3 + 1)",
                AggregationGranularity.Year => $"strftime('%Y-01-01', {date})",
                _ => throw new UnreachableException()
            };

        public override string DateText(string date) => date;

        public override string Negate(string decimalExpression) =>
            $"ef_negate({decimalExpression})";

        public override string Sum(string decimalExpression) => $"ef_sum({decimalExpression})";

        public override string Count => "COUNT(*)";

        public override string CompareDecimal(string left, string operation, string right) =>
            $"ef_compare({left}, {right}) {operation} 0";

        public override string DecimalOrder(string expression) => $"CAST({expression} AS REAL)";

        public override string Like(string expression, string pattern) =>
            $"{expression} LIKE {pattern} ESCAPE '\\'";

        public override string IntegerTotal(string expression) => expression;

        public override string OrderedTextJoin(
            string expression,
            string separator,
            string from,
            string orderBy) =>
            $"(SELECT COALESCE(group_concat(joined_value, '{separator}'), '') " +
            $"FROM (SELECT {expression} AS joined_value FROM {from} ORDER BY {orderBy}))";

        public override object Parameter(TableColumnType type, object value) =>
            type == TableColumnType.Timestamp && value is DateTimeOffset timestamp
                ? timestamp.UtcTicks
                : value;

        public override async Task<object?> ReadValueAsync(
            DbDataReader reader,
            int ordinal,
            TableColumnType type,
            CancellationToken cancellationToken)
        {
            if (await reader.IsDBNullAsync(ordinal, cancellationToken))
            {
                return null;
            }

            return type switch
            {
                TableColumnType.Uuid => reader.GetGuid(ordinal),
                TableColumnType.Text or TableColumnType.Enumeration => reader.GetString(ordinal),
                TableColumnType.Integer => reader.GetInt64(ordinal),
                TableColumnType.Decimal => reader.GetDecimal(ordinal),
                TableColumnType.Boolean => reader.GetBoolean(ordinal),
                TableColumnType.Date => reader.GetFieldValue<DateOnly>(ordinal),
                TableColumnType.Timestamp => new DateTime(
                    reader.GetInt64(ordinal),
                    DateTimeKind.Utc),
                _ => throw new UnreachableException()
            };
        }
    }
}
