using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class NubankPdfInvoiceParserTests
{
    [UnitFact]
    public void GivenSanitizedNubankFixture_WhenParsed_ThenAllSupportedLineFormsAreResolved()
    {
        var invoice = NubankPdfInvoiceParser.ParseTextLines(Fixture());

        Assert.Equal(NubankPdfInvoiceParser.LayoutName, invoice.Layout);
        Assert.Equal(new DateOnly(2027, 2, 10), invoice.DueDate);
        Assert.Equal(new DateOnly(2027, 2, 1), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 12, 14), invoice.PeriodStart);
        Assert.Equal(new DateOnly(2027, 1, 12), invoice.PeriodEnd);
        Assert.Equal(160m, invoice.AmountDue);
        Assert.Equal(invoice.AmountDue, invoice.ParsedAmountDue);
        Assert.Equal(0m, invoice.ReconciliationDifference);

        var installment = Assert.Single(invoice.Lines, line =>
            line.InstallmentNumber.HasValue);
        Assert.Equal((short)2, installment.InstallmentNumber);
        Assert.Equal((short)6, installment.InstallmentCount);
        Assert.Equal("Curso exemplo", installment.Description);

        var foreign = Assert.Single(invoice.Lines, line => line.OriginalAmount.HasValue);
        Assert.Equal(4m, foreign.OriginalAmount);
        Assert.Equal("USD", foreign.OriginalCurrencyCode);
        Assert.Equal(5m, foreign.AppliedRate);

        var tax = Assert.Single(invoice.Lines, line => line.Kind == PdfInvoiceLineKind.Tax);
        var taxReversal = Assert.Single(invoice.Lines, line =>
            line.Kind == PdfInvoiceLineKind.TaxReversal);
        Assert.NotNull(tax.RelatedLineSequence);
        Assert.Equal(tax.RelatedLineSequence, taxReversal.RelatedLineSequence);
        Assert.False(tax.IsUnmatchedReference);
        Assert.Equal(-1m, taxReversal.SignedAmount);

        var reversal = Assert.Single(invoice.Lines, line =>
            line.Kind == PdfInvoiceLineKind.PurchaseReversal);
        Assert.Equal("Mercado exemplo", reversal.OriginalPurchaseReference);
        Assert.NotNull(reversal.RelatedLineSequence);
        Assert.Equal(-5m, reversal.SignedAmount);

        var payment = Assert.Single(invoice.Lines, line =>
            line.Kind == PdfInvoiceLineKind.Payment);
        Assert.Equal(new DateOnly(2027, 1, 5), payment.OccurredOn);
        Assert.Equal(-100m, payment.SignedAmount);
    }

    [UnitFact]
    public void GivenTaxForAbsentPurchase_WhenParsed_ThenStandaloneLineIsFlaggedUnmatched()
    {
        var lines = Fixture().ToList();
        lines.Insert(lines.Count - 1, "11 JAN IOF de \"Compra ausente\" R$ 2,00");
        lines[6] = "IOF de compras internacionais R$ 7,00";
        lines[10] = "Total a pagar R$ 162,00";

        var invoice = NubankPdfInvoiceParser.ParseTextLines(lines);

        var unmatched = Assert.Single(invoice.Lines, line => line.IsUnmatchedReference);
        Assert.Equal(PdfInvoiceLineKind.Tax, unmatched.Kind);
        Assert.Null(unmatched.RelatedLineSequence);
    }

    [UnitFact]
    public void GivenAsciiAndUnicodeMinusSigns_WhenParsed_ThenBothBecomeNegativeAmounts()
    {
        var invoice = NubankPdfInvoiceParser.ParseTextLines(Fixture());

        Assert.Contains(invoice.Lines, line => line.SignedAmount == -5m);
        Assert.Contains(invoice.Lines, line => line.SignedAmount == -1m);
        Assert.Contains(invoice.Lines, line => line.SignedAmount == -10m);
    }

    [UnitFact]
    public void GivenNonReconcilingLines_WhenParsed_ThenBothFiguresAndDifferenceAreReported()
    {
        var lines = Fixture().ToList();
        lines[10] = "Total a pagar R$ 161,00";

        var exception = Assert.Throws<PdfInvoiceParseException>(() =>
            NubankPdfInvoiceParser.ParseTextLines(lines));

        Assert.Contains("parsed amount due 160.00", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stated amount due 161.00", exception.Message, StringComparison.Ordinal);
        Assert.Contains("discrepancy -1.00", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenUnknownLayout_WhenParsed_ThenSupportedLayoutIsNamed()
    {
        var exception = Assert.Throws<PdfInvoiceParseException>(() =>
            NubankPdfInvoiceParser.ParseTextLines(["Another bank", "STATEMENT"]));

        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, exception.Message);
        Assert.Contains(NubankPdfInvoiceParser.LayoutName, exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [UnitFact]
    public void GivenPdfWithoutTextLayer_WhenParsed_ThenOcrIsNotAttempted()
    {
        using var builder = new PdfDocumentBuilder();
        _ = builder.AddPage(PageSize.A4);

        var exception = Assert.Throws<PdfInvoiceParseException>(() =>
            new NubankPdfInvoiceParser().Parse(builder.Build()));

        Assert.Equal(PdfInvoiceImportMessages.NoTextLayer, exception.Message);
    }

    [UnitFact]
    public void GivenTextPdf_WhenParsed_ThenPositionedWordsAreReconstructedIntoLines()
    {
        var content = Pdf(Fixture().Select(line => Ascii(line)
            .Replace('−', '-')
            .Replace("•••• ", string.Empty, StringComparison.Ordinal)));

        var invoice = new NubankPdfInvoiceParser().Parse(content);

        Assert.Equal(8, invoice.Lines.Count);
        Assert.Equal(160m, invoice.ParsedAmountDue);
    }

    private static IReadOnlyList<string> Fixture() =>
    [
        "Nubank",
        "FATURA 10 FEV 2027 EMISSÃO E ENVIO 01 FEV 2027",
        "TRANSAÇÕES",
        "Fatura anterior R$ 100,00",
        "Pagamento recebido −R$ 100,00",
        "Total de compras de todos os cartões, 14 DEZ a 12 JAN R$ 165,00",
        "IOF de compras internacionais R$ 5,00",
        "Outros lançamentos −R$ 10,00",
        "Aviso regulatório que deve ser ignorado",
        "Subtotal do cartão R$ 170,00",
        "Total a pagar R$ 160,00",
        "20 DEZ •••• 1234 Mercado exemplo R$ 100,00",
        "28 DEZ Curso exemplo Parcela 2/6 R$ 50,00",
        "03 JAN Loja exterior R$ 20,00",
        "BRL 20,00 = USD 4,00",
        "Conversão: BRL 5,00 = USD 1 = R$ 5,00",
        "03 JAN IOF de \"Loja exterior\" R$ 6,00",
        "04 JAN IOF de volta de \"Loja exterior\" −R$ 1,00",
        "04 JAN Estorno de \"Mercado exemplo\" -R$ 5,00",
        "05 JAN Crédito de confiança −R$ 10,00",
        "Pagamentos -R$ 100,00",
        "Pagamento em 05 JAN −R$ 100,00"
    ];

    private static byte[] Pdf(IEnumerable<string> lines)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 800;
        foreach (var line in lines)
        {
            page.AddText(line, 9, new PdfPoint(25, y), font);
            y -= 22;
        }

        return builder.Build();
    }

    private static string Ascii(string value) => new(value
        .Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) !=
            UnicodeCategory.NonSpacingMark)
        .ToArray());
}
