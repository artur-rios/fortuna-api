using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;
using ClosedXML.Excel;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class ExcelWorkbookParserTests
{
    private static readonly ExcelColumnMapping Mapping = new(
        "Date", "Amount", "Direction", "Memo", "Category", "Id");

    [UnitFact]
    public void GivenMappedWorkbook_WhenParsed_ThenTypedAndRejectedRowsArePreserved()
    {
        var content = Workbook(sheet =>
        {
            sheet.Cell("A1").Value = "Date";
            sheet.Cell("B1").Value = "Amount";
            sheet.Cell("C1").Value = "Direction";
            sheet.Cell("D1").Value = "Memo";
            sheet.Cell("E1").Value = "Category";
            sheet.Cell("F1").Value = "Id";
            sheet.Cell("A2").Value = new DateTime(2026, 9, 1);
            sheet.Cell("B2").Value = 25.50m;
            sheet.Cell("C2").Value = "expense";
            sheet.Cell("D2").Value = "Lunch";
            sheet.Cell("E2").Value = "Food";
            sheet.Cell("F2").Value = "row-1";
            sheet.Cell("A3").Value = "not-a-date";
            sheet.Cell("B3").Value = "invalid";
            sheet.Cell("C3").Value = "unknown";
        });
        var parser = new ExcelWorkbookParser();

        var validation = parser.Validate(content, Mapping);
        var rows = parser.Parse(content, Mapping).ToArray();

        Assert.True(validation.IsValid);
        Assert.Equal(2, rows.Length);
        Assert.Equal(new DateOnly(2026, 9, 1), rows[0].OccurredOn);
        Assert.Equal(25.50m, rows[0].Amount);
        Assert.Equal(TransactionDirection.Expense, rows[0].Direction);
        Assert.Equal("Food", rows[0].Category);
        Assert.Contains("\"Memo\":\"Lunch\"", rows[0].RawPayload,
            StringComparison.Ordinal);
        Assert.Equal(ExcelImportMessages.RowDateInvalid, rows[1].RejectionReason);
    }

    [UnitFact]
    public void GivenBrazilianValues_WhenParsed_ThenDateAmountAndDirectionAreRecognized()
    {
        var content = Workbook(sheet =>
        {
            sheet.Cell("A1").Value = "Date";
            sheet.Cell("B1").Value = "Amount";
            sheet.Cell("C1").Value = "Direction";
            sheet.Cell("A2").Value = "06/09/2026";
            sheet.Cell("B2").Value = "1.234,56";
            sheet.Cell("C2").Value = "entrada";
        });
        var parser = new ExcelWorkbookParser();

        var row = Assert.Single(parser.Parse(
            content,
            new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null)));

        Assert.Equal(new DateOnly(2026, 9, 6), row.OccurredOn);
        Assert.Equal(1234.56m, row.Amount);
        Assert.Equal(TransactionDirection.Earning, row.Direction);
        Assert.Null(row.RejectionReason);
    }

    [UnitFact]
    public void GivenMissingMappedColumn_WhenValidated_ThenNamedErrorIsReturned()
    {
        var content = Workbook(sheet =>
        {
            sheet.Cell("A1").Value = "Date";
            sheet.Cell("B1").Value = "Amount";
            sheet.Cell("C1").Value = "Direction";
        });

        var result = new ExcelWorkbookParser().Validate(content, Mapping);

        Assert.False(result.IsValid);
        Assert.Equal(ExcelImportMessages.ColumnNotFound("Memo"), result.Error);
    }

    [UnitFact]
    public void GivenUnreadableBytes_WhenValidated_ThenWorkbookIsRejected()
    {
        var result = new ExcelWorkbookParser().Validate(
            [1, 2, 3],
            new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null));

        Assert.False(result.IsValid);
        Assert.Equal(ExcelImportMessages.WorkbookInvalid, result.Error);
    }

    private static byte[] Workbook(Action<IXLWorksheet> populate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Transactions");
        populate(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
