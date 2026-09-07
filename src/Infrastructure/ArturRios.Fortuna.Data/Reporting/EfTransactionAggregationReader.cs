using System.Data;
using System.Data.Common;
using ArturRios.Fortuna.Data.Configuration;
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
        var dimension = ResolveDimension(criteria);
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = BuildSql(dimension);
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
                    reader.GetDecimal(5)));
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

    private static DimensionSql ResolveDimension(TransactionAggregationCriteria criteria) =>
        criteria.Dimension switch
        {
            "period" => Period(criteria.Granularity!),
            "category" when criteria.RollupCategories => new DimensionSql(
                "bucket_category.public_id::text",
                "bucket_category.name",
                "NULL::date",
                "JOIN category_roots root ON root.id = category.id " +
                "JOIN fortuna.category bucket_category ON bucket_category.id = root.root_id",
                "NOT bucket_category.is_deleted"),
            "category" => new DimensionSql(
                "category.public_id::text",
                "category.name",
                "NULL::date"),
            "account" => new DimensionSql(
                "account.public_id::text",
                "account.name",
                "NULL::date",
                Where: "account.id IS NOT NULL AND NOT account.is_deleted"),
            "card" => new DimensionSql(
                "card.public_id::text",
                "card.name",
                "NULL::date",
                Where: "card.id IS NOT NULL AND NOT card.is_deleted"),
            "counterparty" => new DimensionSql(
                "COALESCE(counterparty.public_id::text, 'none')",
                "COALESCE(counterparty.name, 'No counterparty')",
                "NULL::date",
                Where: "counterparty.id IS NULL OR NOT counterparty.is_deleted"),
            "tag" => new DimensionSql(
                "dimension_tag.public_id::text",
                "dimension_tag.name",
                "NULL::date",
                "JOIN fortuna.financial_transaction_tag dimension_link " +
                "ON dimension_link.financial_transaction_id = item.id " +
                "JOIN fortuna.tag dimension_tag ON dimension_tag.id = dimension_link.tag_id",
                "NOT dimension_tag.is_deleted"),
            _ => throw new InvalidOperationException("The aggregation dimension was not normalized.")
        };

    private static DimensionSql Period(string granularity)
    {
        var unit = granularity switch
        {
            "day" => "day",
            "week" => "week",
            "month" => "month",
            "quarter" => "quarter",
            "year" => "year",
            _ => throw new InvalidOperationException(
                "The aggregation granularity was not normalized.")
        };
        var expression = $"date_trunc('{unit}', item.occurred_on::timestamp)::date";
        return new DimensionSql($"({expression})::text", $"({expression})::text", expression);
    }

    private static string BuildSql(DimensionSql dimension)
    {
        var extraWhere = string.IsNullOrWhiteSpace(dimension.Where)
            ? string.Empty
            : $"AND ({dimension.Where})";
        return $"""
            WITH RECURSIVE category_roots AS (
                SELECT item.id, item.id AS root_id
                FROM fortuna.category item
                WHERE item.parent_id IS NULL
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
                           ELSE -item.amount
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
                  AND NOT item.is_deleted
                  AND NOT category.is_deleted
                  AND (account.id IS NULL OR NOT account.is_deleted)
                  AND (card.id IS NULL OR NOT card.is_deleted)
                  AND item.occurred_on BETWEEN @from AND @to
                  AND NOT EXISTS (
                      SELECT 1
                      FROM fortuna.transfer transfer
                      WHERE transfer.outbound_transaction_id = item.id
                         OR transfer.inbound_transaction_id = item.id)
                  AND (@financialAccountId::uuid IS NULL OR
                       account.public_id = @financialAccountId)
                  AND (@creditCardId::uuid IS NULL OR card.public_id = @creditCardId)
                  AND (@categoryId::uuid IS NULL OR category.public_id = @categoryId)
                  AND (@counterpartyId::uuid IS NULL OR
                       counterparty.public_id = @counterpartyId)
                  AND (@tagId::uuid IS NULL OR EXISTS (
                      SELECT 1
                      FROM fortuna.financial_transaction_tag filter_link
                      JOIN fortuna.tag filter_tag ON filter_tag.id = filter_link.tag_id
                      WHERE filter_link.financial_transaction_id = item.id
                        AND filter_tag.public_id = @tagId
                        AND NOT filter_tag.is_deleted))
                  AND (@direction::smallint IS NULL OR item.direction = @direction)
                  AND (@minimumAmount::numeric IS NULL OR
                       item.amount >= @minimumAmount)
                  AND (@maximumAmount::numeric IS NULL OR
                       item.amount <= @maximumAmount)
                  AND (@text::text IS NULL OR
                       item.description ILIKE '%' || @text || '%')
                  {extraWhere}
            )
            SELECT dimension_value,
                   label,
                   bucket_start,
                   currency_code,
                   figure_date,
                   SUM(signed_amount) AS amount
            FROM figures
            GROUP BY dimension_value, label, bucket_start, currency_code, figure_date
            ORDER BY bucket_start NULLS LAST, label, dimension_value, currency_code, figure_date
            """;
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
        AddParameter(command, "text", criteria.Text);
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
