using System.Data;
using System.Data.Common;
using System.Globalization;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Shared.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Reporting;

public sealed class EfTableReportReader(AppDbContext context) : ITableReportReader
{
    private static readonly IReadOnlyDictionary<string, RecordSetDefinition> RecordSets =
        BuildRecordSets().ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

    public async Task<TableReportReadResult> ReadAsync(
        TableReportCriteria criteria,
        CancellationToken cancellationToken)
    {
        if (!RecordSets.TryGetValue(criteria.RecordSet, out var recordSet))
        {
            return new TableReportReadResult(
                TableReportReadOutcome.RecordSetUnknown,
                InvalidName: criteria.RecordSet,
                SupportedValues: RecordSets.Keys.ToArray());
        }

        var selected = new List<ColumnDefinition>();
        foreach (var name in criteria.Columns)
        {
            if (!recordSet.Columns.TryGetValue(name, out var column))
            {
                return new TableReportReadResult(
                    TableReportReadOutcome.ColumnUnknown,
                    InvalidName: name);
            }

            selected.Add(column);
        }

        var preparedFilters = new List<PreparedFilter>();
        foreach (var filter in criteria.Filters)
        {
            if (!recordSet.Columns.TryGetValue(filter.Field, out var column))
            {
                return new TableReportReadResult(
                    TableReportReadOutcome.FilterFieldUnknown,
                    InvalidName: filter.Field);
            }

            var normalizedOperator = NormalizeOperator(filter.Operator);
            if (normalizedOperator is null || !Supports(column.Type, normalizedOperator))
            {
                return new TableReportReadResult(
                    TableReportReadOutcome.FilterOperatorUnknown,
                    InvalidName: column.Name,
                    InvalidOperator: filter.Operator);
            }

            if (!TryParse(column, filter.Value, out var value))
            {
                return new TableReportReadResult(
                    TableReportReadOutcome.FilterValueInvalid,
                    InvalidName: column.Name,
                    InvalidValue: filter.Value);
            }

            preparedFilters.Add(new PreparedFilter(column, normalizedOperator, value!));
        }

        var sorts = new List<(ColumnDefinition Column, bool Descending)>();
        foreach (var sort in criteria.Sorts)
        {
            if (!recordSet.Columns.TryGetValue(sort.Field, out var column))
            {
                return new TableReportReadResult(
                    TableReportReadOutcome.SortFieldUnknown,
                    InvalidName: sort.Field);
            }

            sorts.Add((column, sort.Descending));
        }

        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var where = BuildWhere(recordSet, preparedFilters);
            var totalCount = await CountAsync(
                connection,
                recordSet,
                where,
                criteria.UserId,
                cancellationToken);
            var rows = await ReadRowsAsync(
                connection,
                recordSet,
                selected,
                sorts,
                where,
                criteria,
                cancellationToken);
            var totals = await ReadTotalsAsync(
                connection,
                recordSet,
                selected,
                where,
                criteria.UserId,
                cancellationToken);

            return new TableReportReadResult(
                TableReportReadOutcome.Succeeded,
                new TableReportSnapshot(
                    recordSet.Name,
                    selected.Select(column => new TableColumnSnapshot(
                        column.Name,
                        column.Type,
                        column.TotalExpression is not null,
                        column.CurrencyColumn)).ToArray(),
                    rows,
                    totalCount,
                    criteria.PageNumber,
                    criteria.PageSize,
                    totals));
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> CountAsync(
        DbConnection connection,
        RecordSetDefinition recordSet,
        PreparedWhere where,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {recordSet.From} WHERE {where.Sql}";
        AddParameters(command, userId, where.Parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return checked(Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    private static async Task<IReadOnlyCollection<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        DbConnection connection,
        RecordSetDefinition recordSet,
        IReadOnlyCollection<ColumnDefinition> selected,
        IReadOnlyCollection<(ColumnDefinition Column, bool Descending)> sorts,
        PreparedWhere where,
        TableReportCriteria criteria,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var select = string.Join(", ", selected.Select(column =>
            $"{column.SelectExpression} AS \"{column.Name}\""));
        var order = sorts.Count == 0
            ? recordSet.DefaultOrder
            : string.Join(", ", sorts.Select(sort =>
                $"{sort.Column.FilterExpression} {(sort.Descending ? "DESC" : "ASC")}")) +
              $", {recordSet.TieBreaker}";
        command.CommandText = $"""
            SELECT {select}
            FROM {recordSet.From}
            WHERE {where.Sql}
            ORDER BY {order}
            LIMIT @pageSize OFFSET @offset
            """;
        AddParameters(command, criteria.UserId, where.Parameters);
        AddParameter(command, "pageSize", criteria.PageSize);
        AddParameter(command, "offset", checked((criteria.PageNumber - 1) * criteria.PageSize));

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var column in selected)
            {
                row[column.Name] = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);
                index++;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static async Task<IReadOnlyCollection<TableTotalGroupSnapshot>> ReadTotalsAsync(
        DbConnection connection,
        RecordSetDefinition recordSet,
        IReadOnlyCollection<ColumnDefinition> selected,
        PreparedWhere where,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var totals = new List<TableTotalGroupSnapshot>();
        foreach (var column in selected.Where(column => column.TotalExpression is not null))
        {
            await using var command = connection.CreateCommand();
            var currency = column.CurrencyExpression ?? "NULL::text";
            var figureDate = column.FigureDateExpression ?? "NULL::date";
            command.CommandText = $"""
                SELECT {currency} AS currency_code,
                       {figureDate} AS figure_date,
                       COALESCE(SUM({column.TotalExpression}), 0) AS total_value
                FROM {recordSet.From}
                WHERE {where.Sql}
                GROUP BY {currency}, {figureDate}
                ORDER BY {currency}, {figureDate}
                """;
            AddParameters(command, userId, where.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                totals.Add(new TableTotalGroupSnapshot(
                    column.Name,
                    await reader.IsDBNullAsync(0, cancellationToken)
                        ? null
                        : reader.GetString(0),
                    await reader.IsDBNullAsync(1, cancellationToken)
                        ? null
                        : reader.GetFieldValue<DateOnly>(1),
                    reader.GetDecimal(2)));
            }
        }

        return totals;
    }

    private static PreparedWhere BuildWhere(
        RecordSetDefinition recordSet,
        IReadOnlyCollection<PreparedFilter> filters)
    {
        var clauses = new List<string> { recordSet.OwnerPredicate, recordSet.LivePredicate };
        var parameters = new List<(string Name, object Value)>();
        var index = 0;
        foreach (var filter in filters)
        {
            var parameter = $"filter{index++}";
            var expression = filter.Operator switch
            {
                "eq" => $"{filter.Column.FilterExpression} = @{parameter}",
                "ne" => $"{filter.Column.FilterExpression} <> @{parameter}",
                "gt" => $"{filter.Column.FilterExpression} > @{parameter}",
                "gte" => $"{filter.Column.FilterExpression} >= @{parameter}",
                "lt" => $"{filter.Column.FilterExpression} < @{parameter}",
                "lte" => $"{filter.Column.FilterExpression} <= @{parameter}",
                "contains" => $"{filter.Column.FilterExpression} ILIKE @{parameter}",
                "startsWith" => $"{filter.Column.FilterExpression} ILIKE @{parameter}",
                "endsWith" => $"{filter.Column.FilterExpression} ILIKE @{parameter}",
                _ => throw new InvalidOperationException("A filter operator was not normalized.")
            };
            var value = filter.Operator switch
            {
                "contains" => $"%{EscapeLike((string)filter.Value)}%",
                "startsWith" => $"{EscapeLike((string)filter.Value)}%",
                "endsWith" => $"%{EscapeLike((string)filter.Value)}",
                _ => filter.Value
            };
            if (filter.Operator is "contains" or "startsWith" or "endsWith")
            {
                expression += " ESCAPE E'\\\\'";
            }

            clauses.Add(expression);
            parameters.Add((parameter, value));
        }

        return new PreparedWhere(string.Join(" AND ", clauses), parameters);
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? NormalizeOperator(string value) => value.Trim().ToLowerInvariant() switch
    {
        "eq" or "equals" => "eq",
        "ne" or "notequals" => "ne",
        "gt" => "gt",
        "gte" => "gte",
        "lt" => "lt",
        "lte" => "lte",
        "contains" => "contains",
        "startswith" => "startsWith",
        "endswith" => "endsWith",
        _ => null
    };

    private static bool Supports(TableColumnType type, string operation) => type switch
    {
        TableColumnType.Text => operation is "eq" or "ne" or "contains" or "startsWith" or
            "endsWith",
        TableColumnType.Uuid or TableColumnType.Boolean or TableColumnType.Enumeration =>
            operation is "eq" or "ne",
        TableColumnType.Integer or TableColumnType.Decimal or TableColumnType.Date or
            TableColumnType.Timestamp => operation is "eq" or "ne" or "gt" or "gte" or "lt" or
                "lte",
        _ => false
    };

    private static bool TryParse(ColumnDefinition column, string value, out object? parsed)
    {
        parsed = null;
        switch (column.Type)
        {
            case TableColumnType.Text:
                parsed = value;
                return true;
            case TableColumnType.Uuid when Guid.TryParse(value, out var guid):
                parsed = guid;
                return true;
            case TableColumnType.Integer when long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integer):
                parsed = integer;
                return true;
            case TableColumnType.Decimal when decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number):
                parsed = number;
                return true;
            case TableColumnType.Boolean when bool.TryParse(value, out var boolean):
                parsed = boolean;
                return true;
            case TableColumnType.Date when DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date):
                parsed = date;
                return true;
            case TableColumnType.Timestamp when DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp):
                parsed = timestamp;
                return true;
            case TableColumnType.Enumeration when column.EnumValues is not null &&
                                                  column.EnumValues.TryGetValue(value, out var item):
                parsed = item;
                return true;
            default:
                return false;
        }
    }

    private static void AddParameters(
        DbCommand command,
        Guid userId,
        IReadOnlyCollection<(string Name, object Value)> filters)
    {
        AddParameter(command, "userId", userId);
        foreach (var (name, value) in filters)
        {
            AddParameter(command, name, value);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static IEnumerable<RecordSetDefinition> BuildRecordSets()
    {
        yield return Define("transactions",
            "fortuna.financial_transaction r " +
            "JOIN fortuna.currency currency ON currency.id = r.currency_id " +
            "LEFT JOIN fortuna.financial_account account ON account.id = r.financial_account_id " +
            "LEFT JOIN fortuna.credit_card card ON card.id = r.credit_card_id " +
            "JOIN fortuna.category category ON category.id = r.category_id " +
            "LEFT JOIN fortuna.counterparty counterparty ON counterparty.id = r.counterparty_id",
            "r.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted",
            "r.occurred_on DESC, r.public_id",
            "r.public_id",
            Uuid("id", "r.public_id"),
            Uuid("accountId", "account.public_id"),
            Text("accountName", "account.name"),
            Uuid("creditCardId", "card.public_id"),
            Text("creditCardName", "card.name"),
            Uuid("categoryId", "category.public_id"),
            Text("categoryName", "category.name"),
            Uuid("counterpartyId", "counterparty.public_id"),
            Text("counterpartyName", "counterparty.name"),
            Enum("direction", "r.direction", (1, "Expense"), (2, "Earning")),
            Money("amount", "r.amount", "currency.code", "r.occurred_on", "currencyCode"),
            Text("currencyCode", "currency.code"),
            Date("occurredOn", "r.occurred_on"),
            Text("description", "r.description"),
            Enum("sourceType", "r.source_type", (1, "Manual"), (2, "Pluggy"),
                (3, "Excel"), (4, "Pdf")),
            Boolean("isReconciled", "r.is_reconciled"),
            Boolean("isManuallyCorrected", "r.is_manually_corrected"),
            Boolean("isLateArriving", "r.is_late_arriving"),
            Boolean("isPossibleDuplicate", "r.is_possible_duplicate"),
            Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("accounts",
            "fortuna.financial_account r JOIN fortuna.currency currency ON currency.id = r.currency_id",
            Owner(), Live(), "r.name, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Text("name", "r.name"),
            Text("institution", "r.institution"),
            Enum("accountType", "r.account_type", (1, "Checking"), (2, "Savings"),
                (3, "Cash"), (4, "Other")),
            Text("currencyCode", "currency.code"),
            Money("openingBalance", "r.opening_balance", "currency.code", "CURRENT_DATE",
                "currencyCode"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("creditCards",
            "fortuna.credit_card r JOIN fortuna.currency currency ON currency.id = r.currency_id",
            Owner(), Live(), "r.name, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Text("name", "r.name"), Text("issuer", "r.issuer"),
            Text("currencyCode", "currency.code"),
            Money("creditLimit", "r.credit_limit", "currency.code", "CURRENT_DATE", "currencyCode"),
            Integer("closingDay", "r.closing_day"), Integer("dueDay", "r.due_day"),
            Text("lastFourDigits", "r.last_four_digits"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("investments",
            "fortuna.investment r JOIN fortuna.currency currency ON currency.id = r.currency_id",
            Owner(), Live(), "r.instrument, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Text("instrument", "r.instrument"),
            Text("institution", "r.institution"),
            Enum("investmentType", "r.investment_type", (1, "FixedIncome"), (2, "Equity"),
                (3, "Fund"), (4, "Other")),
            Text("currencyCode", "currency.code"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("categories",
            "fortuna.category r LEFT JOIN fortuna.category parent ON parent.id = r.parent_id",
            Owner(), Live(), "r.name, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("parentId", "parent.public_id"),
            Text("parentName", "parent.name"), Text("name", "r.name"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("tags", "fortuna.tag r", Owner(), Live(), "r.name, r.public_id",
            "r.public_id", Uuid("id", "r.public_id"), Text("name", "r.name"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("counterparties", "fortuna.counterparty r", Owner(), Live(),
            "r.name, r.public_id", "r.public_id", Uuid("id", "r.public_id"),
            Text("name", "r.name"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("budgets",
            "fortuna.budget r JOIN fortuna.currency currency ON currency.id = r.currency_id",
            Owner(), Live(), "r.period_start DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"),
            Money("amount", "r.amount", "currency.code", "r.period_start", "currencyCode"),
            Text("currencyCode", "currency.code"),
            Enum("periodType", "r.period_type", (1, "Monthly"), (2, "Quarterly"),
                (3, "Yearly")),
            Date("periodStart", "r.period_start"),
            Boolean("includeDescendants", "r.include_descendants"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("goals",
            "fortuna.goal r JOIN fortuna.currency currency ON currency.id = r.currency_id",
            Owner(), Live(), "r.target_date, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Text("name", "r.name"),
            Money("targetAmount", "r.target_amount", "currency.code", "r.target_date",
                "currencyCode"),
            Text("currencyCode", "currency.code"), Date("targetDate", "r.target_date"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("creditCardStatements",
            "fortuna.credit_card_statement r " +
            "JOIN fortuna.credit_card card ON card.id = r.credit_card_id " +
            "JOIN fortuna.currency currency ON currency.id = card.currency_id " +
            "LEFT JOIN fortuna.financial_transaction settlement " +
            "ON settlement.id = r.settlement_transaction_id",
            "card.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT card.is_deleted",
            "r.period_start DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("creditCardId", "card.public_id"),
            Text("creditCardName", "card.name"), Text("currencyCode", "currency.code"),
            Date("periodStart", "r.period_start"), Date("periodEnd", "r.period_end"),
            Date("closingDate", "r.closing_date"), Date("dueDate", "r.due_date"),
            Money("previousBalance", "r.previous_balance", "currency.code", "r.due_date",
                "currencyCode"),
            Money("paymentsReceived", "r.payments_received", "currency.code", "r.due_date",
                "currencyCode"),
            Money("purchaseTotal", "r.purchase_total", "currency.code", "r.due_date",
                "currencyCode"),
            Money("foreignTaxTotal", "r.foreign_tax_total", "currency.code", "r.due_date",
                "currencyCode"),
            Money("otherEntries", "r.other_entries", "currency.code", "r.due_date",
                "currencyCode"),
            Money("amountDue", "r.amount_due", "currency.code", "r.due_date", "currencyCode"),
            Enum("status", "r.status", (1, "Open"), (2, "Closed"), (3, "Settled")),
            Uuid("settlementTransactionId", "settlement.public_id"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("investmentMovements",
            "fortuna.investment_movement r " +
            "JOIN fortuna.investment investment ON investment.id = r.investment_id " +
            "JOIN fortuna.currency currency ON currency.id = investment.currency_id",
            "investment.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT investment.is_deleted",
            "r.occurred_on DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("investmentId", "investment.public_id"),
            Text("instrument", "investment.instrument"),
            Enum("movementType", "r.movement_type", (1, "Contribution"), (2, "Withdrawal"),
                (3, "Yield"), (4, "Fee")),
            Money("amount", "r.amount", "currency.code", "r.occurred_on", "currencyCode"),
            Text("currencyCode", "currency.code"), Date("occurredOn", "r.occurred_on"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("investmentValuations",
            "fortuna.investment_valuation r " +
            "JOIN fortuna.investment investment ON investment.id = r.investment_id " +
            "JOIN fortuna.currency currency ON currency.id = investment.currency_id",
            "investment.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT investment.is_deleted",
            "r.valued_on DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("investmentId", "investment.public_id"),
            Text("instrument", "investment.instrument"),
            Money("value", "r.value", "currency.code", "r.valued_on", "currencyCode"),
            Text("currencyCode", "currency.code"), Date("valuedOn", "r.valued_on"),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("recurringTransactions",
            "fortuna.recurring_transaction r " +
            "JOIN fortuna.currency currency ON currency.id = r.currency_id " +
            "LEFT JOIN fortuna.financial_account account ON account.id = r.financial_account_id " +
            "LEFT JOIN fortuna.credit_card card ON card.id = r.credit_card_id " +
            "JOIN fortuna.category category ON category.id = r.category_id " +
            "LEFT JOIN fortuna.counterparty counterparty ON counterparty.id = r.counterparty_id",
            Owner(), Live(), "r.starts_on, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("accountId", "account.public_id"),
            Text("accountName", "account.name"), Uuid("creditCardId", "card.public_id"),
            Text("creditCardName", "card.name"), Uuid("categoryId", "category.public_id"),
            Text("categoryName", "category.name"),
            Uuid("counterpartyId", "counterparty.public_id"),
            Text("counterpartyName", "counterparty.name"),
            Enum("direction", "r.direction", (1, "Expense"), (2, "Earning")),
            Money("amount", "r.amount", "currency.code", "r.starts_on", "currencyCode"),
            Text("currencyCode", "currency.code"),
            Enum("frequency", "r.frequency", (1, "Weekly"), (2, "Monthly"),
                (3, "Quarterly"), (4, "Yearly")),
            Date("startsOn", "r.starts_on"), Date("endsOn", "r.ends_on"),
            Date("lastMaterializedOn", "r.last_materialized_on"),
            Text("description", "r.description"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("installmentPlans",
            "fortuna.installment_plan r " +
            "JOIN fortuna.credit_card card ON card.id = r.credit_card_id " +
            "JOIN fortuna.currency currency ON currency.id = card.currency_id",
            "card.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT card.is_deleted",
            "r.purchased_on DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("creditCardId", "card.public_id"),
            Text("creditCardName", "card.name"),
            Money("totalAmount", "r.total_amount", "currency.code", "r.purchased_on",
                "currencyCode"),
            Text("currencyCode", "currency.code"), Integer("installmentCount", "r.installment_count"),
            Date("purchasedOn", "r.purchased_on"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("transfers",
            "fortuna.transfer r " +
            "JOIN fortuna.financial_transaction outbound ON outbound.id = r.outbound_transaction_id " +
            "LEFT JOIN fortuna.financial_transaction inbound ON inbound.id = r.inbound_transaction_id " +
            "LEFT JOIN fortuna.investment_movement movement " +
            "ON movement.id = r.inbound_investment_movement_id",
            "outbound.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT outbound.is_deleted",
            "outbound.occurred_on DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("outboundTransactionId", "outbound.public_id"),
            Uuid("inboundTransactionId", "inbound.public_id"),
            Uuid("inboundInvestmentMovementId", "movement.public_id"),
            Date("occurredOn", "outbound.occurred_on"),
            Decimal("appliedRate", "r.applied_rate"),
            Date("rateDate", "r.rate_date"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("attachments",
            "fortuna.attachment r " +
            "JOIN fortuna.financial_transaction tx ON tx.id = r.transaction_id",
            "tx.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)",
            "NOT r.is_deleted AND NOT tx.is_deleted",
            "r.created_at DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("transactionId", "tx.public_id"),
            Text("fileName", "r.file_name"), Text("contentType", "r.content_type"),
            Integer("sizeInBytes", "r.size_in_bytes"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));

        yield return Define("connections", "fortuna.connection r", Owner(), "TRUE",
            "r.created_at DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"),
            Enum("dataSourceType", "r.data_source_type", (1, "Manual"), (2, "Pluggy"),
                (3, "Excel"), (4, "Pdf")),
            Text("externalReference", "r.external_reference"),
            Enum("status", "r.status", (1, "Active"), (2, "RequiresReauthentication"),
                (3, "Revoked")),
            Timestamp("createdAt", "r.created_at"), Timestamp("updatedAt", "r.updated_at"));

        yield return Define("importJobs",
            "fortuna.import_job r LEFT JOIN fortuna.connection connection " +
            "ON connection.id = r.connection_id",
            Owner(), "TRUE", "r.created_at DESC, r.public_id", "r.public_id",
            Uuid("id", "r.public_id"), Uuid("connectionId", "connection.public_id"),
            Enum("sourceType", "r.source_type", (1, "Manual"), (2, "Pluggy"),
                (3, "Excel"), (4, "Pdf")),
            Enum("status", "r.status", (1, "Pending"), (2, "Running"),
                (3, "Completed"), (4, "Failed")),
            Date("periodStart", "r.period_start"), Date("periodEnd", "r.period_end"),
            Integer("importedCount", "r.imported_count"),
            Integer("duplicateCount", "r.duplicate_count"),
            Integer("rejectedCount", "r.rejected_count"),
            Text("failureReason", "r.failure_reason"), Timestamp("createdAt", "r.created_at"),
            Timestamp("updatedAt", "r.updated_at"));
    }

    private static string Owner() =>
        "r.user_id = (SELECT id FROM fortuna.\"user\" WHERE public_id = @userId)";

    private static string Live() => "NOT r.is_deleted";

    private static RecordSetDefinition Define(
        string name,
        string from,
        string ownerPredicate,
        string livePredicate,
        string defaultOrder,
        string tieBreaker,
        params ColumnDefinition[] columns) => new(
            name,
            from,
            ownerPredicate,
            livePredicate,
            defaultOrder,
            tieBreaker,
            columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase));

    private static ColumnDefinition Uuid(string name, string expression) =>
        new(name, expression, expression, TableColumnType.Uuid);

    private static ColumnDefinition Text(string name, string expression) =>
        new(name, expression, expression, TableColumnType.Text);

    private static ColumnDefinition Integer(string name, string expression) =>
        new(
            name,
            expression,
            expression,
            TableColumnType.Integer,
            TotalExpression: $"({expression})::numeric");

    private static ColumnDefinition Decimal(string name, string expression) =>
        new(
            name,
            expression,
            expression,
            TableColumnType.Decimal,
            TotalExpression: expression);

    private static ColumnDefinition Boolean(string name, string expression) =>
        new(name, expression, expression, TableColumnType.Boolean);

    private static ColumnDefinition Date(string name, string expression) =>
        new(name, expression, expression, TableColumnType.Date);

    private static ColumnDefinition Timestamp(string name, string expression) =>
        new(name, expression, expression, TableColumnType.Timestamp);

    private static ColumnDefinition Money(
        string name,
        string expression,
        string currencyExpression,
        string figureDateExpression,
        string currencyColumn) => new(
            name,
            expression,
            expression,
            TableColumnType.Decimal,
            TotalExpression: expression,
            CurrencyExpression: currencyExpression,
            FigureDateExpression: figureDateExpression,
            CurrencyColumn: currencyColumn);

    private static ColumnDefinition Enum(
        string name,
        string expression,
        params (short Value, string Name)[] values)
    {
        var select = "CASE " + expression + " " + string.Join(" ", values.Select(value =>
            $"WHEN {value.Value} THEN '{value.Name}'")) + " END";
        return new ColumnDefinition(
            name,
            select,
            expression,
            TableColumnType.Enumeration,
            EnumValues: values.ToDictionary(
                value => value.Name,
                value => value.Value,
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record RecordSetDefinition(
        string Name,
        string From,
        string OwnerPredicate,
        string LivePredicate,
        string DefaultOrder,
        string TieBreaker,
        IReadOnlyDictionary<string, ColumnDefinition> Columns);

    private sealed record ColumnDefinition(
        string Name,
        string SelectExpression,
        string FilterExpression,
        TableColumnType Type,
        string? TotalExpression = null,
        string? CurrencyExpression = null,
        string? FigureDateExpression = null,
        string? CurrencyColumn = null,
        IReadOnlyDictionary<string, short>? EnumValues = null);

    private sealed record PreparedFilter(
        ColumnDefinition Column,
        string Operator,
        object Value);

    private sealed record PreparedWhere(
        string Sql,
        IReadOnlyCollection<(string Name, object Value)> Parameters);
}
