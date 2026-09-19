using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class SharedValidationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData("12.3456", true)]
    [InlineData("12.34567", false)]
    [InlineData("999999999999999.9999", true)]
    [InlineData("1000000000000000", false)]
    [InlineData("-999999999999999", true)]
    public void GivenAmount_WhenCheckedForMoneyStorage_ThenPrecisionAndMagnitudeAreEnforced(
        string amount,
        bool expected)
    {
        Assert.Equal(expected, RuleBuilderExtensions.FitsMoneyStorage(decimal.Parse(amount,
            System.Globalization.CultureInfo.InvariantCulture)));
    }

    [UnitTheory]
    [InlineData(" brl ", true)]
    [InlineData("USD", true)]
    [InlineData("US", false)]
    [InlineData("U5D", false)]
    [InlineData("  US", false)]
    [InlineData(null, false)]
    public void GivenCode_WhenCheckedAsCurrency_ThenThreeTrimmedLettersAreRequired(
        string? code,
        bool expected)
    {
        Assert.Equal(expected, RuleBuilderExtensions.IsCurrencyCode(code));
    }

    [UnitFact]
    public async Task GivenOverPreciseBudgetAmount_WhenValidated_ThenPrecisionIsRejected()
    {
        var result = await new CreateBudgetCommandValidator().ValidateAsync(new CreateBudgetCommand
        {
            Amount = 10.12345m,
            CurrencyCode = " brl ",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = new DateOnly(2026, 9, 1),
            CategoryIds = [Guid.NewGuid()]
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(BudgetMessages.AmountPrecisionInvalid, error.ErrorMessage);
    }

    [UnitFact]
    public async Task GivenPaddedTwoLetterBudgetCurrency_WhenValidated_ThenCurrencyIsRejected()
    {
        var result = await new UpdateBudgetCommandValidator().ValidateAsync(new UpdateBudgetCommand
        {
            Amount = 10m,
            CurrencyCode = " BR",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = new DateOnly(2026, 9, 1),
            CategoryIds = [Guid.NewGuid()]
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(BudgetMessages.CurrencyInvalid, error.ErrorMessage);
    }

    [UnitFact]
    public async Task GivenOversizedGoalTarget_WhenValidated_ThenPrecisionIsRejected()
    {
        var result = await new UpdateGoalCommandValidator().ValidateAsync(new UpdateGoalCommand
        {
            Name = "Trip",
            TargetAmount = 1_000_000_000_000_000m,
            CurrencyCode = "BRL",
            TargetDate = new DateOnly(2027, 1, 1),
            AccountIds = [Guid.NewGuid()]
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(GoalMessages.TargetAmountPrecisionInvalid, error.ErrorMessage);
    }

    [UnitFact]
    public async Task GivenPaymentDateTwoDaysAhead_WhenSettlementValidated_ThenItIsRejected()
    {
        var validator = new SettleCreditCardStatementCommandValidator(new FixedTimeProvider(Now));

        var result = await validator.ValidateAsync(new SettleCreditCardStatementCommand
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            Amount = 10m,
            PaymentDate = new DateOnly(2026, 9, 6)
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(CreditCardStatementMessages.PaymentDateTooFarInFuture, error.ErrorMessage);
    }

    [UnitFact]
    public async Task GivenOverlongImportFileNames_WhenValidated_ThenFileNamesAreRejected()
    {
        var fileName = new string('a', 301);

        var excel = await new ImportExcelWorkbookCommandValidator(new ExcelImportOptions(1024))
            .ValidateAsync(new ImportExcelWorkbookCommand
            {
                TargetId = Guid.NewGuid(),
                TargetType = ImportTargetType.Account,
                FileName = fileName,
                Content = [1],
                Mapping = new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null)
            });
        var pdf = await new ImportPdfInvoiceCommandValidator(new PdfInvoiceImportOptions(1024))
            .ValidateAsync(new ImportPdfInvoiceCommand
            {
                CreditCardId = Guid.NewGuid(),
                FileName = fileName,
                Content = [1]
            });

        Assert.Contains(excel.Errors, error => error.ErrorMessage == ExcelImportMessages.FileNameTooLong);
        Assert.Contains(pdf.Errors, error => error.ErrorMessage == PdfInvoiceImportMessages.FileNameTooLong);
    }

    [UnitFact]
    public async Task GivenPaddedNameWithinBoundOnceTrimmed_WhenValidated_ThenItIsAccepted()
    {
        var name = "  " + new string('t', 200) + "  ";

        var tag = await new CreateTagCommandValidator().ValidateAsync(new CreateTagCommand { Name = name });
        var counterparty = await new CreateCounterpartyCommandValidator()
            .ValidateAsync(new CreateCounterpartyCommand { Name = name });

        Assert.True(tag.IsValid);
        Assert.True(counterparty.IsValid);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
