using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordManualExchangeRateCommandValidatorTests
{
    private readonly RecordManualExchangeRateCommandValidator validator = new();

    [UnitTheory]
    [InlineData("", "USD", ManualExchangeRateMessages.BaseCurrencyRequired)]
    [InlineData("US", "BRL", ManualExchangeRateMessages.BaseCurrencyInvalid)]
    [InlineData("USD", "", ManualExchangeRateMessages.QuoteCurrencyRequired)]
    [InlineData("USD", "REAL", ManualExchangeRateMessages.QuoteCurrencyInvalid)]
    public async Task GivenInvalidCurrencyShape_WhenValidated_ThenFieldErrorIsReturned(
        string baseCode,
        string quoteCode,
        string expectedError)
    {
        var result = await validator.ValidateAsync(ValidCommand(baseCode, quoteCode));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.ErrorMessage == expectedError);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("-0.00000001")]
    public async Task GivenNonPositiveRate_WhenValidated_ThenRateErrorIsReturned(string rate)
    {
        var command = ValidCommand();
        command.Rate = decimal.Parse(rate);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == ManualExchangeRateMessages.RateMustBePositive);
    }

    [UnitTheory]
    [InlineData("123456789012.12345678")]
    [InlineData("1.123456789")]
    public async Task GivenRateOutsideStoragePrecision_WhenValidated_ThenPrecisionErrorIsReturned(string rate)
    {
        var command = ValidCommand();
        command.Rate = decimal.Parse(rate);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == ManualExchangeRateMessages.RatePrecisionInvalid);
    }

    [UnitFact]
    public async Task GivenSameCurrenciesIgnoringCase_WhenValidated_ThenPairErrorIsReturned()
    {
        var result = await validator.ValidateAsync(ValidCommand(" usd ", "USD"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == ManualExchangeRateMessages.CurrenciesMustDiffer);
    }

    [UnitFact]
    public async Task GivenMissingDate_WhenValidated_ThenDateErrorIsReturned()
    {
        var command = ValidCommand();
        command.RateDate = default;

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == ManualExchangeRateMessages.RateDateRequired);
    }

    private static RecordManualExchangeRateCommand ValidCommand(
        string baseCode = "USD",
        string quoteCode = "BRL") => new()
        {
            BaseCurrencyCode = baseCode,
            QuoteCurrencyCode = quoteCode,
            Rate = 5.25m,
            RateDate = new DateOnly(2026, 9, 4)
        };
}
