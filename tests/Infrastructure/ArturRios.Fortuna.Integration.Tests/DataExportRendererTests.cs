using System.Text;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Integration.Exports;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Util.Test.Attributes;
using ClosedXML.Excel;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class DataExportRendererTests
{
    private static readonly DataExportDocument Document = new(
        "transactions",
        [
            new TableColumnSnapshot("description", TableColumnType.Text, false, null),
            new TableColumnSnapshot("amount", TableColumnType.Decimal, true, "currencyCode"),
            new TableColumnSnapshot("currencyCode", TableColumnType.Text, false, null)
        ],
        [new Dictionary<string, object?>
        {
            ["description"] = "Coffee; beans",
            ["amount"] = 12.34m,
            ["currencyCode"] = "BRL"
        }],
        [new DataExportTotal("amount", "BRL", 12.34m, true)],
        "pt-BR");

    [UnitFact]
    public void GivenLocaleAndDecimal_WhenCsvRendered_ThenExactLocalizedTextIsWritten()
    {
        var result = new DataExportRenderer().Render(Document, DataExportFormat.Csv);

        var text = Encoding.UTF8.GetString(result.Content);
        Assert.Equal("csv", result.Extension);
        Assert.Contains("\"Coffee; beans\";12,34;BRL", text);
        Assert.Contains("amount;BRL;12,34", text);
    }

    [UnitFact]
    public void GivenDocument_WhenExcelRendered_ThenWorkbookContainsTextFormattedDecimal()
    {
        var result = new DataExportRenderer().Render(Document, DataExportFormat.Excel);

        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Export");
        Assert.Equal("12,34", sheet.Cell(2, 2).GetString());
        Assert.Equal("BRL", sheet.Cell(2, 3).GetString());
    }

    [UnitFact]
    public void GivenDocument_WhenPdfRendered_ThenValidHeaderAndTableTextAreWritten()
    {
        var result = new DataExportRenderer().Render(Document, DataExportFormat.Pdf);

        var text = Encoding.ASCII.GetString(result.Content);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("description | amount | currencyCode", text);
        Assert.Contains("Coffee; beans | 12,34 | BRL", text);
        Assert.EndsWith("%%EOF", text);
    }

    [UnitFact]
    public void GivenTextStartingWithFormulaTrigger_WhenCsvRendered_ThenCellIsNeutralized()
    {
        var document = SingleTextDocument("=HYPERLINK(\"http://evil\")", "@SUM(A1)", "+1", "-2",
            "\tTab");

        var result = new DataExportRenderer().Render(document, DataExportFormat.Csv);

        var text = Encoding.UTF8.GetString(result.Content);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", text);
        Assert.Contains("'@SUM(A1)", text);
        Assert.Contains("'+1", text);
        Assert.Contains("'-2", text);
        Assert.Contains("'\tTab", text);
    }

    [UnitFact]
    public void GivenNegativeDecimal_WhenCsvRendered_ThenNumberIsNotPrefixed()
    {
        var document = Document with
        {
            Rows =
            [
                new Dictionary<string, object?>
                {
                    ["description"] = "Refund",
                    ["amount"] = -5.5m,
                    ["currencyCode"] = "BRL"
                }
            ]
        };

        var result = new DataExportRenderer().Render(document, DataExportFormat.Csv);

        var text = Encoding.UTF8.GetString(result.Content);
        Assert.Contains("Refund;-5,5;BRL", text);
        Assert.DoesNotContain("'-5,5", text);
    }

    [UnitFact]
    public void GivenPortugueseText_WhenPdfRendered_ThenLatin1BytesAndWinAnsiFontAreWritten()
    {
        var document = SingleTextDocument("Pão de açúcar ✓");

        var result = new DataExportRenderer().Render(document, DataExportFormat.Pdf);

        var text = Encoding.Latin1.GetString(result.Content);
        Assert.Contains("/Encoding /WinAnsiEncoding", text);
        Assert.Contains("(Pão de açúcar ?) Tj", text);
    }

    private static DataExportDocument SingleTextDocument(params string[] values) => new(
        "transactions",
        [new TableColumnSnapshot("description", TableColumnType.Text, false, null)],
        values.Select(value => (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["description"] = value }).ToArray(),
        [],
        "pt-BR");
}
