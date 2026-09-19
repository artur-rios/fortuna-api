using System.Data;
using System.Data.Common;
using System.Diagnostics;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Shared.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Reporting;

public sealed class EfTransactionAggregationReader(AppDbContext context)
    : ITransactionAggregationReader
{
    public async Task<IReadOnlyCollection<TransactionAggregationFigureSnapshot>> ReadAsync(
        TransactionAggregationCriteria criteria,
        CancellationToken cancellationToken)
    {
        var dialect = ReportingSqlDialect.For(context.Database);
        var dimension = ResolveDimension(criteria, dialect);
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = BuildSql(dimension, criteria.Selections, dialect);
            AddParameters(command, criteria);
            var figures = new List<TransactionAggregationFigureSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                figures.Add(new TransactionAggregationFigureSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    await reader.IsDBNullAsync(2, cancellationToken)
                        ? null
                        : reader.GetFieldValue<DateOnly>(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.GetDecimal(5),
                    reader.GetInt32(6)));
            }

            return figures;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static DimensionSql ResolveDimension(
        TransactionAggregationCriteria criteria,
        ReportingSqlDialect dialect) =>
        criteria.Dimension switch
        {
            AggregationDimension.Period => Period(criteria.Granularity!.Value, dialect),
            AggregationDimension.Category when criteria.RollupCategories => new DimensionSql(
                dialect.UuidText("bucket_category.public_id"),
                "bucket_category.name",
                dialect.NullDate,
                "JOIN category_roots root ON root.id = category.id " +
                "JOIN fortuna.category bucket_category ON bucket_category.id = root.root_id",
                "NOT bucket_category.is_deleted"),
            AggregationDimension.Category => new DimensionSql(
                dialect.UuidText("category.public_id"),
                "category.name",
                dialect.NullDate),
            AggregationDimension.Account => new DimensionSql(
                dialect.UuidText("account.public_id"),
                "account.name",
                dialect.NullDate,
                Where: "account.id IS NOT NULL AND NOT account.is_deleted"),
            AggregationDimension.Card => new DimensionSql(
                dialect.UuidText("card.public_id"),
                "card.name",
                dialect.NullDate,
                Where: "card.id IS NOT NULL AND NOT card.is_deleted"),
            AggregationDimension.Counterparty => new DimensionSql(
                $"COALESCE({dialect.UuidText("counterparty.public_id")}, 'none')",
                "COALESCE(counterparty.name, 'No counterparty')",
                dialect.NullDate,
                Where: "counterparty.id IS NULL OR NOT counterparty.is_deleted"),
            AggregationDimension.Tag => new DimensionSql(
                dialect.UuidText("dimension_tag.public_id"),
                "dimension_tag.name",
                dialect.NullDate,
                "JOIN fortuna.financial_transaction_tag dimension_link " +
                "ON dimension_link.financial_transaction_id = item.id " +
                "JOIN fortuna.tag dimension_tag ON dimension_tag.id = dimension_link.tag_id",
                "NOT dimension_tag.is_deleted"),
            _ => throw new UnreachableException()
        };

    private static DimensionSql Period(
        AggregationGranularity granularity,
        ReportingSqlDialect dialect)
    {
        var expression = dialect.PeriodStart("item.occurred_on", granularity);
        var text = dialect.DateText(expression);

        return new DimensionSql(text, text, expression);
    }

    private static string BuildSql(
        DimensionSql dimension,
        IReadOnlyCollection<TransactionAggregationSelection> selections,
        ReportingSqlDialect dialect)
    {
        var extraWhere = string.IsNullOrWhiteSpace(dimension.Where)
            ? string.Empty
            : $"AND ({dimension.Where})";
        var selectionWhere = BuildSelectionWhere(selections);
        var live = TransactionVisibility.LiveSql("item", "category", "account", "card");
        var notTransfer = TransactionVisibility.NotTransferSql("item", "fortuna.transfer");
        var minimum = dialect.CompareDecimal("item.amount", ">=", "@minimumAmount");
        var maximum = dialect.CompareDecimal("item.amount", "<=", "@maximumAmount");

        return dialect.QualifyTables($"""
            WITH RECURSIVE category_roots AS (
                SELECT item.id, item.id AS root_id
                FROM fortuna.category item
                WHERE item.parent_id IS NULL
                  AND item.user_id = (
                      SELECT id FROM fortuna."user" WHERE public_id = @userId)
                UNION ALL
                SELECT child.id, parent.root_id
                FROM fortuna.category child
                JOIN category_roots parent ON parent.id = child.parent_id
            ), figures AS (
                SELECT {dimension.Value} AS dimension_value,
                       {dimension.Label} AS label,
                       {dimension.BucketStart} AS bucket_start,
                       currency.code AS currency_code,
                       item.occurred_on AS figure_date,
                       CASE item.direction
                           WHEN 2 THEN item.amount
                           ELSE {dialect.Negate("item.amount")}
                       END AS signed_amount
                FROM fortuna.financial_transaction item
                JOIN fortuna."user" owner ON owner.id = item.user_id
                JOIN fortuna.currency currency ON currency.id = item.currency_id
                JOIN fortuna.category category ON category.id = item.category_id
                LEFT JOIN fortuna.financial_account account
                    ON account.id = item.financial_account_id
                LEFT JOIN fortuna.credit_card card ON card.id = item.credit_card_id
                LEFT JOIN fortuna.counterparty counterparty
                    ON counterparty.id = item.counterparty_id
                {dimension.Joins}
                WHERE owner.public_id = @userId
                  AND {live}
                  AND {notTransfer}
                  AND item.occurred_on BETWEEN @from AND @to
                  AND ({dialect.TypedParameter("financialAccountId", "uuid")} IS NULL OR
                       account.public_id = @financialAccountId)
                  AND ({dialect.TypedParameter("creditCardId", "uuid")} IS NULL OR
                       card.public_id = @creditCardId)
                  AND ({dialect.TypedParameter("categoryId", "uuid")} IS NULL OR
                       category.public_id = @categoryId)
                  AND ({dialect.TypedParameter("counterpartyId", "uuid")} IS NULL OR
                       counterparty.public_id = @counterpartyId)
                  AND ({dialect.TypedParameter("tagId", "uuid")} IS NULL OR EXISTS (
                      SELECT 1
                      FROM fortuna.financial_transaction_tag filter_link
                      JOIN fortuna.tag filter_tag ON filter_tag.id = filter_link.tag_id
                      WHERE filter_link.financial_transaction_id = item.id
                        AND filter_tag.public_id = @tagId
                        AND NOT filter_tag.is_deleted))
                  AND ({dialect.TypedParameter("direction", "smallint")} IS NULL OR
                       item.direction = @direction)
                  AND ({dialect.TypedParameter("minimumAmount", "numeric")} IS NULL OR
                       {minimum})
                  AND ({dialect.TypedParameter("maximumAmount", "numeric")} IS NULL OR
                       {maximum})
                  AND ({dialect.TypedParameter("text", "text")} IS NULL OR
                       {dialect.Like("item.description", "@text")})
                  {extraWhere}
                  {selectionWhere}
            )
            SELECT dimension_value,
                   label,
                   bucket_start,
                   currency_code,
                   figure_date,
                   {dialect.Sum("signed_amount")} AS amount,
                   {dialect.Count} AS record_count
            FROM figures
            GROUP BY dimension_value, label, bucket_start, currency_code, figure_date
            ORDER BY bucket_start NULLS LAST, label, dimension_value, currency_code, figure_date
            """);
    }

    private static void AddParameters(
        DbCommand command,
        TransactionAggregationCriteria criteria)
    {
        AddParameter(command, "userId", criteria.UserId);
        AddParameter(command, "from", criteria.From);
        AddParameter(command, "to", criteria.To);
        AddParameter(command, "financialAccountId", criteria.FinancialAccountId);
        AddParameter(command, "creditCardId", criteria.CreditCardId);
        AddParameter(command, "categoryId", criteria.CategoryId);
        AddParameter(command, "tagId", criteria.TagId);
        AddParameter(command, "counterpartyId", criteria.CounterpartyId);
        AddParameter(command, "direction", criteria.Direction.HasValue
            ? (short)criteria.Direction.Value
            : null);
        AddParameter(command, "minimumAmount", criteria.MinimumAmount);
        AddParameter(command, "maximumAmount", criteria.MaximumAmount);
        AddParameter(command, "text", criteria.Text is null
            ? null
            : SqlLike.Contains(criteria.Text));
        var index = 0;
        foreach (var selection in criteria.Selections)
        {
            if (selection.Dimension == AggregationDimension.Period)
            {
                AddParameter(command, $"selection{index}From", selection.From);
                AddParameter(command, $"selection{index}To", selection.To);
            }
            else if (selection.Dimension != AggregationDimension.Counterparty ||
                     selection.Value != "none")
            {
                AddParameter(command, $"selection{index}Value", Guid.Parse(selection.Value));
            }

            index++;
        }
    }

    private static string BuildSelectionWhere(
        IReadOnlyCollection<TransactionAggregationSelection> selections)
    {
        var clauses = new List<string>();
        var index = 0;
        foreach (var selection in selections)
        {
            var value = $"@selection{index}Value";
            clauses.Add(selection.Dimension switch
            {
                AggregationDimension.Period =>
                    $"item.occurred_on BETWEEN @selection{index}From AND @selection{index}To",
                AggregationDimension.Category when selection.RollupCategories =>
                    $"category.id IN (SELECT root.id FROM category_roots root " +
                    $"WHERE root.root_id = (SELECT id FROM fortuna.category " +
                    $"WHERE public_id = {value}))",
                AggregationDimension.Category => $"category.public_id = {value}",
                AggregationDimension.Account => $"account.public_id = {value}",
                AggregationDimension.Card => $"card.public_id = {value}",
                AggregationDimension.Counterparty when selection.Value == "none" =>
                    "counterparty.id IS NULL",
                AggregationDimension.Counterparty => $"counterparty.public_id = {value}",
                AggregationDimension.Tag =>
                    $"EXISTS (SELECT 1 FROM fortuna.financial_transaction_tag key_link " +
                    $"JOIN fortuna.tag key_tag ON key_tag.id = key_link.tag_id " +
                    $"WHERE key_link.financial_transaction_id = item.id " +
                    $"AND key_tag.public_id = {value} AND NOT key_tag.is_deleted)",
                _ => throw new UnreachableException()
            });
            index++;
        }

        return clauses.Count == 0
            ? string.Empty
            : "AND " + string.Join(" AND ", clauses.Select(clause => $"({clause})"));
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record DimensionSql(
        string Value,
        string Label,
        string BucketStart,
        string Joins = "",
        string? Where = null);
}
