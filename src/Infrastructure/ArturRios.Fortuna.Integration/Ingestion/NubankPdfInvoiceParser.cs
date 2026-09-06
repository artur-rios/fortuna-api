using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed partial class NubankPdfInvoiceParser : IPdfInvoiceParser
{
    public const string LayoutName = "Nubank credit card invoice";
    private const decimal ReconciliationTolerance = 0.01m;

    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly IReadOnlyDictionary<string, int> Months =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["JAN"] = 1,
            ["FEV"] = 2,
            ["MAR"] = 3,
            ["ABR"] = 4,
            ["MAI"] = 5,
            ["JUN"] = 6,
            ["JUL"] = 7,
            ["AGO"] = 8,
            ["SET"] = 9,
            ["OUT"] = 10,
            ["NOV"] = 11,
            ["DEZ"] = 12
        };

    public ParsedPdfInvoice Parse(byte[] content)
    {
        IReadOnlyList<InvoiceTextLine> lines;
        try
        {
            using var document = PdfDocument.Open(content);
            lines = document.GetPages().SelectMany(Lines).ToArray();
        }
        catch (Exception exception) when (exception is not PdfInvoiceParseException)
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.FileInvalid);
        }

        if (lines.Count == 0)
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.NoTextLayer);
        }

        return ParseLines(lines);
    }

    internal static ParsedPdfInvoice ParseTextLines(IEnumerable<string> textLines) =>
        ParseLines(textLines.Select((text, index) => new InvoiceTextLine(1, index, Clean(text))).ToArray());

    private static ParsedPdfInvoice ParseLines(IReadOnlyList<InvoiceTextLine> lines)
    {
        var text = string.Join('\n', lines.Select(line => line.Text));
        if (!Contains(text, "Nubank") || !Contains(text, "FATURA") ||
            !Contains(text, "TRANSAÇÕES"))
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.UnsupportedLayout);
        }

        var dueDate = HeaderDate(text, DueDateRegex());
        var issueDate = HeaderDate(text, IssueDateRegex());
        var period = BillingPeriod(lines, issueDate);
        var previousBalance = RequiredSummary(lines, PreviousBalanceRegex());
        var paymentsReceived = Math.Abs(RequiredSummary(lines, PaymentsReceivedRegex()));
        var purchaseTotal = RequiredSummary(lines, PurchaseTotalRegex());
        var foreignTaxTotal = OptionalSummary(lines, ForeignTaxTotalRegex());
        var otherEntries = OptionalSummary(lines, OtherEntriesRegex());
        var amountDue = RequiredSummary(lines, AmountDueRegex());
        var parsedLines = TransactionLines(lines, period.Start, period.End);
        if (parsedLines.Count == 0)
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.InvoiceIncomplete);
        }

        var parsedAmountDue = previousBalance + parsedLines.Sum(line => line.SignedAmount);
        var difference = decimal.Round(parsedAmountDue - amountDue, 2);
        if (Math.Abs(difference) > ReconciliationTolerance)
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.ReconciliationFailed(
                parsedAmountDue,
                amountDue,
                difference));
        }

        return new ParsedPdfInvoice(
            LayoutName,
            dueDate,
            issueDate,
            period.Start,
            period.End,
            previousBalance,
            paymentsReceived,
            purchaseTotal,
            foreignTaxTotal,
            otherEntries,
            amountDue,
            parsedAmountDue,
            difference,
            parsedLines);
    }

    private static IReadOnlyList<InvoiceTextLine> Lines(Page page)
    {
        var words = page.GetWords().OrderByDescending(word => word.BoundingBox.Bottom).ToArray();
        var groups = new List<List<Word>>();
        foreach (var word in words)
        {
            var group = groups.FirstOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.BoundingBox.Bottom) -
                    word.BoundingBox.Bottom) <= 2.5);
            if (group is null)
            {
                group = [];
                groups.Add(group);
            }

            group.Add(word);
        }

        return groups
            .OrderByDescending(group => group.Average(word => word.BoundingBox.Bottom))
            .Select((group, index) => new InvoiceTextLine(
                page.Number,
                index,
                Clean(string.Join(' ', group.OrderBy(word => word.BoundingBox.Left)
                    .Select(word => word.Text)))))
            .Where(line => line.Text.Length > 0)
            .ToArray();
    }

    private static DateOnly HeaderDate(string text, Regex expression)
    {
        var match = expression.Match(text);
        if (!match.Success || !TryDate(
                match.Groups["day"].Value,
                match.Groups["month"].Value,
                match.Groups["year"].Value,
                out var date))
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.InvoiceIncomplete);
        }

        return date;
    }

    private static (DateOnly Start, DateOnly End) BillingPeriod(
        IEnumerable<InvoiceTextLine> lines,
        DateOnly issueDate)
    {
        var line = lines.Select(item => item.Text).FirstOrDefault(value =>
            Contains(value, "Total de compras") && PeriodRegex().IsMatch(value));
        var match = line is null ? Match.Empty : PeriodRegex().Match(line);
        if (!match.Success || !Month(match.Groups["startMonth"].Value, out var startMonth) ||
            !Month(match.Groups["endMonth"].Value, out var endMonth))
        {
            throw new PdfInvoiceParseException(PdfInvoiceImportMessages.InvoiceIncomplete);
        }

        var startDay = int.Parse(match.Groups["startDay"].Value, CultureInfo.InvariantCulture);
        var endDay = int.Parse(match.Groups["endDay"].Value, CultureInfo.InvariantCulture);
        foreach (var endYear in new[] { issueDate.Year, issueDate.Year - 1 })
        {
            var startYear = startMonth > endMonth ? endYear - 1 : endYear;
            if (!TryDate(startDay, startMonth, startYear, out var start) ||
                !TryDate(endDay, endMonth, endYear, out var end))
            {
                continue;
            }

            if (start <= end && end <= issueDate && end.DayNumber - start.DayNumber <= 62)
            {
                return (start, end);
            }
        }

        throw new PdfInvoiceParseException(PdfInvoiceImportMessages.InvoiceIncomplete);
    }

    private static decimal RequiredSummary(IEnumerable<InvoiceTextLine> lines, Regex expression)
    {
        foreach (var line in lines.Select(item => item.Text))
        {
            var match = expression.Match(line);
            if (match.Success && TryAmount(match.Groups["amount"].Value, out var amount))
            {
                return amount;
            }
        }

        throw new PdfInvoiceParseException(PdfInvoiceImportMessages.InvoiceIncomplete);
    }

    private static decimal OptionalSummary(IEnumerable<InvoiceTextLine> lines, Regex expression)
    {
        foreach (var line in lines.Select(item => item.Text))
        {
            var match = expression.Match(line);
            if (match.Success && TryAmount(match.Groups["amount"].Value, out var amount))
            {
                return amount;
            }
        }

        return 0m;
    }

    private static IReadOnlyList<ParsedPdfInvoiceLine> TransactionLines(
        IReadOnlyList<InvoiceTextLine> source,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var builders = new List<ParsedLineBuilder>();
        ParsedLineBuilder? previous = null;
        foreach (var item in source)
        {
            var match = TransactionRegex().Match(item.Text);
            var paymentMatch = PaymentRegex().Match(item.Text);
            if (!match.Success && paymentMatch.Success)
            {
                match = paymentMatch;
            }

            if (match.Success && TryOccurredOn(
                    match.Groups["day"].Value,
                    match.Groups["month"].Value,
                    periodStart,
                    periodEnd,
                    out var occurredOn) &&
                TryAmount(match.Groups["amount"].Value, out var signedAmount))
            {
                var description = Clean(match.Groups["description"].Value);
                if (Ignored(description) || SummaryDescription(description))
                {
                    continue;
                }

                var builder = new ParsedLineBuilder(
                    builders.Count + 1,
                    item,
                    occurredOn,
                    Card(match),
                    description,
                    signedAmount);
                builders.Add(builder);
                previous = builder;
                continue;
            }

            var undated = UndatedSpecialLineRegex().Match(item.Text);
            if (undated.Success && previous is not null &&
                TryAmount(undated.Groups["amount"].Value, out signedAmount))
            {
                var builder = new ParsedLineBuilder(
                    builders.Count + 1,
                    item,
                    previous.OccurredOn,
                    null,
                    Clean(undated.Groups["description"].Value),
                    signedAmount);
                builders.Add(builder);
                previous = builder;
                continue;
            }

            if (previous is not null && ApplyForeignDetails(previous, item.Text))
            {
                previous.RawLines.Add(item.Text);
            }
        }

        foreach (var line in builders)
        {
            line.Classify();
            if (line.Kind is not (PdfInvoiceLineKind.Tax or PdfInvoiceLineKind.TaxReversal or
                PdfInvoiceLineKind.PurchaseReversal))
            {
                continue;
            }

            line.OriginalPurchaseReference = PurchaseReference(line.Description);
            var related = FindRelatedPurchase(builders, line);
            line.RelatedLineSequence = related?.Sequence;
            line.IsUnmatchedReference = related is null;
        }

        return builders.Select(builder => builder.Build()).ToArray();
    }

    private static ParsedLineBuilder? FindRelatedPurchase(
        IEnumerable<ParsedLineBuilder> candidates,
        ParsedLineBuilder reference)
    {
        var name = NormalizeReference(reference.OriginalPurchaseReference);
        if (name.Length == 0)
        {
            return null;
        }

        return candidates
            .Where(candidate => candidate.Kind == PdfInvoiceLineKind.Purchase &&
                candidate.Sequence != reference.Sequence)
            .OrderByDescending(candidate => candidate.Sequence < reference.Sequence)
            .ThenBy(candidate => Math.Abs(candidate.Sequence - reference.Sequence))
            .FirstOrDefault(candidate => NormalizeReference(candidate.Description).Contains(
                name,
                StringComparison.Ordinal) || name.Contains(
                NormalizeReference(candidate.Description),
                StringComparison.Ordinal));
    }

    private static bool ApplyForeignDetails(ParsedLineBuilder line, string text)
    {
        var rateMatch = ForeignRateRegex().Match(text);
        if (rateMatch.Success &&
            TryUnsignedForeignNumber(rateMatch.Groups["rate"].Value, out var statedRate))
        {
            line.OriginalCurrencyCode = rateMatch.Groups["currency"].Value.ToUpperInvariant();
            line.AppliedRate = statedRate;
            return line.OriginalCurrencyCode != "BRL";
        }

        var detailMatch = ForeignDetailRegex().Match(text);
        if (!detailMatch.Success ||
            !TryUnsignedForeignNumber(detailMatch.Groups["converted"].Value,
                out var convertedAmount) ||
            !TryUnsignedForeignNumber(detailMatch.Groups["original"].Value,
                out var originalAmount))
        {
            return false;
        }

        line.OriginalCurrencyCode = detailMatch.Groups["currency"].Value.ToUpperInvariant();
        line.OriginalAmount = originalAmount;
        line.AppliedRate ??= decimal.Round(convertedAmount / originalAmount, 8);
        return line.OriginalCurrencyCode != "BRL";
    }

    private static bool TryOccurredOn(
        string dayText,
        string monthText,
        DateOnly periodStart,
        DateOnly periodEnd,
        out DateOnly date)
    {
        date = default;
        if (!int.TryParse(dayText, out var day) || !Month(monthText, out var month))
        {
            return false;
        }

        foreach (var year in Enumerable.Range(periodStart.Year - 1,
                     periodEnd.Year - periodStart.Year + 3))
        {
            if (TryDate(day, month, year, out var candidate) &&
                candidate >= periodStart && candidate <= periodEnd)
            {
                date = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryDate(string day, string month, string year, out DateOnly date)
    {
        date = default;
        return int.TryParse(day, out var parsedDay) && Month(month, out var parsedMonth) &&
            int.TryParse(year, out var parsedYear) &&
            TryDate(parsedDay, parsedMonth, parsedYear, out date);
    }

    private static bool TryDate(int day, int month, int year, out DateOnly date)
    {
        date = default;
        if (day < 1 || month is < 1 or > 12 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static bool Month(string value, out int month) =>
        Months.TryGetValue(RemoveDiacritics(value.Trim()).ToUpperInvariant(), out month);

    private static bool TryAmount(string value, out decimal amount)
    {
        var normalized = Clean(value).Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('−', '-').Replace(" ", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
            BrazilianCulture,
            out amount);
    }

    private static bool TryUnsignedForeignNumber(string value, out decimal amount)
    {
        var culture = value.Contains(',') ? BrazilianCulture : CultureInfo.InvariantCulture;
        return decimal.TryParse(value, NumberStyles.Number, culture, out amount) && amount > 0m;
    }

    private static string? Card(Match match)
    {
        var value = match.Groups["card"].Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? value.Trim() : digits;
    }

    private static bool Ignored(string description) =>
        Contains(description, "subtotal") || Contains(description, "total do cartão") ||
        Contains(description, "total deste cartão");

    private static bool SummaryDescription(string description) =>
        Contains(description, "fatura anterior") || Contains(description, "pagamento recebido") ||
        Contains(description, "total de compras") || Contains(description, "total a pagar") ||
        Contains(description, "outros lançamentos");

    private static string? PurchaseReference(string description)
    {
        var quoted = QuotedReferenceRegex().Match(description);
        if (quoted.Success)
        {
            return Clean(quoted.Groups["reference"].Value);
        }

        var value = ReferencePrefixRegex().Replace(description, string.Empty).Trim(' ', ':', '-', '–');
        return value.Length == 0 ? null : value;
    }

    private static string NormalizeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(RemoveDiacritics(value).ToUpperInvariant()
            .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            .ToArray()).Trim();
    }

    private static bool Contains(string value, string expected) =>
        RemoveDiacritics(value).Contains(RemoveDiacritics(expected), StringComparison.OrdinalIgnoreCase);

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC);
    }

    private static string Clean(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    private sealed record InvoiceTextLine(int Page, int Position, string Text);

    private sealed class ParsedLineBuilder(
        int sequence,
        InvoiceTextLine source,
        DateOnly occurredOn,
        string? maskedCardNumber,
        string description,
        decimal signedAmount)
    {
        public int Sequence { get; } = sequence;
        public DateOnly OccurredOn { get; } = occurredOn;
        public string? MaskedCardNumber { get; } = maskedCardNumber;
        public string Description { get; private set; } = description;
        public decimal SignedAmount { get; } = signedAmount;
        public PdfInvoiceLineKind Kind { get; private set; }
        public short? InstallmentNumber { get; private set; }
        public short? InstallmentCount { get; private set; }
        public decimal? OriginalAmount { get; set; }
        public string? OriginalCurrencyCode { get; set; }
        public decimal? AppliedRate { get; set; }
        public int? RelatedLineSequence { get; set; }
        public string? OriginalPurchaseReference { get; set; }
        public bool IsUnmatchedReference { get; set; }
        public List<string> RawLines { get; } = [source.Text];

        public void Classify()
        {
            var installment = InstallmentRegex().Match(Description);
            if (installment.Success && short.TryParse(installment.Groups["number"].Value,
                    out var number) && short.TryParse(installment.Groups["count"].Value,
                    out var count) && number >= 1 && count >= 2 && number <= count)
            {
                InstallmentNumber = number;
                InstallmentCount = count;
                Description = Clean(InstallmentRegex().Replace(Description, string.Empty));
            }

            Kind = Contains(Description, "pagamento")
                ? PdfInvoiceLineKind.Payment
                : Contains(Description, "IOF de volta") ||
                  (Contains(Description, "IOF") && SignedAmount < 0m)
                    ? PdfInvoiceLineKind.TaxReversal
                    : Contains(Description, "IOF")
                        ? PdfInvoiceLineKind.Tax
                        : Contains(Description, "estorno") || Contains(Description, "reversão")
                            ? PdfInvoiceLineKind.PurchaseReversal
                            : Contains(Description, "ajuste") || Contains(Description, "crédito")
                                ? PdfInvoiceLineKind.CreditAdjustment
                                : PdfInvoiceLineKind.Purchase;
        }

        public ParsedPdfInvoiceLine Build() => new(
            Sequence,
            JsonSerializer.Serialize(new
            {
                layout = LayoutName,
                source.Page,
                source.Position,
                lines = RawLines,
                maskedCardNumber = MaskedCardNumber,
                kind = Kind.ToString(),
                relatedLineSequence = RelatedLineSequence,
                originalPurchaseReference = OriginalPurchaseReference,
                isUnmatchedReference = IsUnmatchedReference
            }),
            OccurredOn,
            MaskedCardNumber,
            Description,
            SignedAmount,
            Kind,
            InstallmentNumber,
            InstallmentCount,
            OriginalAmount,
            OriginalCurrencyCode,
            AppliedRate,
            RelatedLineSequence,
            OriginalPurchaseReference,
            IsUnmatchedReference);
    }

    [GeneratedRegex(@"FATURA\s+(?<day>\d{1,2})\s+(?<month>[A-ZÇ]{3})\s+(?<year>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DueDateRegex();

    [GeneratedRegex(@"EMISS[ÃA]O\s+E\s+ENVIO\s+(?<day>\d{1,2})\s+(?<month>[A-ZÇ]{3})\s+(?<year>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex IssueDateRegex();

    [GeneratedRegex(@"(?<startDay>\d{1,2})\s+(?<startMonth>[A-ZÇ]{3})\s+A\s+(?<endDay>\d{1,2})\s+(?<endMonth>[A-ZÇ]{3})", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Fatura\s+anterior.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PreviousBalanceRegex();

    [GeneratedRegex(@"Pagamentos?\s+recebidos?.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentsReceivedRegex();

    [GeneratedRegex(@"Total\s+de\s+compras.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PurchaseTotalRegex();

    [GeneratedRegex(@"IOF\s+de\s+compras\s+internacionais.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignTaxTotalRegex();

    [GeneratedRegex(@"Outros\s+lan[çc]amentos.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OtherEntriesRegex();

    [GeneratedRegex(@"Total\s+a\s+pagar.*?(?<amount>[−-]?\s*R\$\s*[\d.]+,\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountDueRegex();

    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<month>[A-ZÇ]{3})\s+(?:(?<card>(?:[•*Xx]{4}\s*)?\d{4})\s+)?(?<description>.+?)\s+(?<amount>(?:[−-]\s*)?R\$\s*[\d.]+,\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex TransactionRegex();

    [GeneratedRegex(@"^(?<description>Pagamento\s+em\s+(?<day>\d{1,2})\s+(?<month>[A-ZÇ]{3}).*?)\s+(?<amount>(?:[−-]\s*)?R\$\s*[\d.]+,\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentRegex();

    [GeneratedRegex(@"^(?<description>(?:IOF|Estorno|Revers[aã]o|Ajuste|Cr[eé]dito).+?)\s+(?<amount>(?:[−-]\s*)?R\$\s*[\d.]+,\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex UndatedSpecialLineRegex();

    [GeneratedRegex(@"(?:^|\s)Parcela\s+(?<number>\d{1,2})/(?<count>\d{1,2})(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex InstallmentRegex();

    [GeneratedRegex(@"^BRL\s*(?<converted>[\d.]+[,\.]\d+)\s*=\s*(?<currency>[A-Z]{3})\s*(?<original>[\d.]+[,\.]\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignDetailRegex();

    [GeneratedRegex(@"^Convers[aã]o:\s*BRL\s*(?<rate>[\d.]+[,\.]\d+)\s*=\s*(?<currency>[A-Z]{3})\s*1(?:[,\.]0+)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignRateRegex();

    [GeneratedRegex("[\"“](?<reference>.+?)[\"”]")]
    private static partial Regex QuotedReferenceRegex();

    [GeneratedRegex(@"^(?:IOF\s+de\s+volta|IOF\s+de|Estorno\s+de|Revers[aã]o\s+de)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePrefixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
