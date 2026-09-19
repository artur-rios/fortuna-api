using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ConvertFigureQueryValidatorTests
{
    private readonly ConvertFigureQueryValidator validator = new();

    [UnitFact]
    public async Task GivenAValidFigure_WhenValidated_ThenItIsAccepted()
    {
        var result = await validator.ValidateAsync(Query());

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenNoDisplayCurrency_WhenValidated_ThenProfileFallbackIsAccepted()
    {
        var query = Query();
        query.DisplayCurrencyCode = null;

        var result = await validator.ValidateAsync(query);

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenInvalidDisplayCurrency_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.DisplayCurrencyCode = "US";

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.DisplayCurrencyInvalid);
    }

    [UnitFact]
    public async Task GivenMissingFigureDate_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.FigureDate = default;

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.FigureDateRequired);
    }

    [UnitFact]
    public async Task GivenNullAmounts_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.Amounts = null;

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.AmountsRequired);
    }

    [UnitFact]
    public async Task GivenInvalidAmountCurrency_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.Amounts = [new FigureAmountInput { Amount = 1m, CurrencyCode = "U" }];

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.AmountCurrencyInvalid);
    }

    [UnitFact]
    public async Task GivenExcessAmountScale_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.Amounts = [new FigureAmountInput { Amount = 1.00001m, CurrencyCode = "USD" }];

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.AmountPrecisionInvalid);
    }

    [UnitFact]
    public async Task GivenNullAmountElement_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.Amounts = [null!];

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.AmountRequired);
    }

    [UnitFact]
    public async Task GivenTooManyAmounts_WhenValidated_ThenItIsRejected()
    {
        var query = Query();
        query.Amounts = Enumerable.Range(0, ConvertFigureQueryValidator.MaximumAmounts + 1)
            .Select(_ => new FigureAmountInput { Amount = 1m, CurrencyCode = "USD" })
            .ToArray();

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == FigureConversionMessages.TooManyAmounts);
    }

    [UnitTheory]
    [InlineData(" usd ", true)]
    [InlineData("US ", false)]
    [InlineData("U5D", false)]
    public async Task GivenPaddedAmountCurrency_WhenValidated_ThenTheTrimmedCodeIsChecked(
        string code,
        bool isValid)
    {
        var query = Query();
        query.Amounts = [new FigureAmountInput { Amount = 1m, CurrencyCode = code }];

        var result = await validator.ValidateAsync(query);

        Assert.Equal(isValid, result.IsValid);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GivenBlankDisplayCurrency_WhenValidated_ThenProfileFallbackIsAccepted(string code)
    {
        var query = Query();
        query.DisplayCurrencyCode = code;

        var result = await validator.ValidateAsync(query);

        Assert.True(result.IsValid);
    }

    private static ConvertFigureQuery Query() => new()
    {
        DisplayCurrencyCode = "BRL",
        FigureDate = new DateOnly(2026, 9, 4),
        Amounts = [new FigureAmountInput { Amount = 1.25m, CurrencyCode = "USD" }]
    };
}
