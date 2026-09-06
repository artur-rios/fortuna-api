using System.Globalization;
using System.Text.Json;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ClosedXML.Excel;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed class ExcelWorkbookParser : IExcelWorkbookParser
{
    public ExcelWorkbookValidation Validate(byte[] content, ExcelColumnMapping mapping)
    {
        try
        {
            using var workbook = Open(content);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            var header = worksheet?.FirstRowUsed();
            if (header is null)
            {
                return Invalid();
            }

            var columns = Headers(header);
            foreach (var mapped in MappedColumns(mapping))
            {
                if (!columns.ContainsKey(mapped))
                {
                    return new ExcelWorkbookValidation(
                        false,
                        ExcelImportMessages.ColumnNotFound(mapped));
                }
            }

            return new ExcelWorkbookValidation(true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Invalid();
        }
    }

    public IReadOnlyCollection<ExcelWorkbookRow> Parse(
        byte[] content,
        ExcelColumnMapping mapping)
    {
        using var workbook = Open(content);
        var worksheet = workbook.Worksheets.First();
        var header = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException(ExcelImportMessages.WorkbookInvalid);
        var columns = Headers(header);
        var rows = new List<ExcelWorkbookRow>();
        foreach (var row in worksheet.RowsUsed().Where(row => row.RowNumber() > header.RowNumber()))
        {
            if (columns.Values.All(number => row.Cell(number).IsEmpty()))
            {
                continue;
            }

            var raw = columns.ToDictionary(
                pair => pair.Key,
                pair => Text(row.Cell(pair.Value)),
                StringComparer.OrdinalIgnoreCase);
            var date = Date(row.Cell(columns[mapping.Date.Trim()]));
            var amount = Amount(row.Cell(columns[mapping.Amount.Trim()]));
            var direction = Direction(row.Cell(columns[mapping.Direction.Trim()]));
            var rejection = !date.HasValue
                ? ExcelImportMessages.RowDateInvalid
                : amount is not > 0
                    ? ExcelImportMessages.RowAmountInvalid
                    : !direction.HasValue
                        ? ExcelImportMessages.RowDirectionInvalid
                        : null;
            rows.Add(new ExcelWorkbookRow(
                row.RowNumber(),
                JsonSerializer.Serialize(raw),
                date,
                amount is > 0 ? amount : null,
                direction,
                Optional(row, columns, mapping.Description),
                Optional(row, columns, mapping.Category),
                Optional(row, columns, mapping.ExternalId),
                rejection));
        }

        return rows;
    }

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content, false));

    private static ExcelWorkbookValidation Invalid() =>
        new(false, ExcelImportMessages.WorkbookInvalid);

    private static Dictionary<string, int> Headers(IXLRow header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in header.CellsUsed())
        {
            var name = Text(cell).Trim();
            if (name.Length == 0 || !columns.TryAdd(name, cell.Address.ColumnNumber))
            {
                throw new InvalidOperationException(ExcelImportMessages.WorkbookInvalid);
            }
        }

        return columns;
    }

    private static IEnumerable<string> MappedColumns(ExcelColumnMapping mapping) =>
        new[]
        {
            mapping.Date,
            mapping.Amount,
            mapping.Direction,
            mapping.Description,
            mapping.Category,
            mapping.ExternalId
        }.Where(column => !string.IsNullOrWhiteSpace(column)).Select(column => column!.Trim());

    private static string? Optional(
        IXLRow row,
        IReadOnlyDictionary<string, int> columns,
        string? mappedColumn)
    {
        if (string.IsNullOrWhiteSpace(mappedColumn))
        {
            return null;
        }

        var value = Text(row.Cell(columns[mappedColumn.Trim()])).Trim();
        return value.Length == 0 ? null : value;
    }

    private static DateOnly? Date(IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var typed))
        {
            return DateOnly.FromDateTime(typed);
        }

        var text = Text(cell).Trim();
        var firstCulture = text.Contains('/')
            ? CultureInfo.GetCultureInfo("pt-BR")
            : CultureInfo.InvariantCulture;
        if (DateOnly.TryParse(text, firstCulture, DateTimeStyles.None, out var date) ||
            DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }

        return null;
    }

    private static decimal? Amount(IXLCell cell)
    {
        if (cell.TryGetValue<decimal>(out var typed))
        {
            return typed;
        }

        var text = Text(cell).Trim();
        var firstCulture = text.Contains(',')
            ? CultureInfo.GetCultureInfo("pt-BR")
            : CultureInfo.InvariantCulture;
        return decimal.TryParse(text, NumberStyles.Number, firstCulture, out var amount) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            ? amount
            : null;
    }

    private static TransactionDirection? Direction(IXLCell cell) =>
        Text(cell).Trim().ToUpperInvariant() switch
        {
            "EXPENSE" or "DEBIT" or "OUTFLOW" or "SAÍDA" or "SAIDA" or "1" =>
                TransactionDirection.Expense,
            "EARNING" or "INCOME" or "CREDIT" or "INFLOW" or "ENTRADA" or "2" =>
                TransactionDirection.Earning,
            _ => null
        };

    private static string Text(IXLCell cell) => cell.GetFormattedString(CultureInfo.InvariantCulture);
}
