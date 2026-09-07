using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class AggregateTransactionsQueryValidatorTests
{
    private readonly AggregateTransactionsQueryValidator validator = new(
        new TransactionAggregationOptions(366));

    [UnitTheory]
    [InlineData("period", "day")]
    [InlineData("period", "week")]
    [InlineData("period", "month")]
    [InlineData("period", "quarter")]
    [InlineData("period", "year")]
    [InlineData("category", null)]
    [InlineData("account", null)]
    [InlineData("card", null)]
    [InlineData("counterparty", null)]
    [InlineData("tag", null)]
    public async Task GivenSupportedDimensionAndGranularity_WhenValidated_ThenSucceeds(
        string dimension,
        string? granularity)
    {
        var query = Valid();
        query.Dimension = dimension;
        query.Granularity = granularity;

        var result = await validator.ValidateAsync(query);

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenMissingUnknownOrOversizedCriteria_WhenValidated_ThenSpecificErrorsReturn()
    {
        var query = Valid();
        query.Dimension = "unknown";
        query.Granularity = "hour";
        query.From = new DateOnly(2025, 1, 1);
        query.To = new DateOnly(2026, 1, 2);
        query.FinancialAccountId = Guid.Empty;
        query.Direction = (TransactionDirection)99;
        query.MinimumAmount = 2m;
        query.MaximumAmount = 1m;
        query.DisplayCurrencyCode = "US";

        var result = await validator.ValidateAsync(query);

        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("Dimension 'unknown'"));
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("Granularity 'hour'"));
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.MaximumSpanExceeded(366));
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.FinancialAccountIdInvalid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.DirectionInvalid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.AmountRangeInvalid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.DisplayCurrencyInvalid);
    }

    [UnitFact]
    public async Task GivenPeriodWithoutDatesOrGranularity_WhenValidated_ThenRequiredErrorsReturn()
    {
        var result = await validator.ValidateAsync(new AggregateTransactionsQuery
        {
            Dimension = "period"
        });

        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.GranularityRequired);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.FromRequired);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionAggregationMessages.ToRequired);
    }

    private static AggregateTransactionsQuery Valid() => new()
    {
        Dimension = "period",
        Granularity = "month",
        From = new DateOnly(2026, 1, 1),
        To = new DateOnly(2026, 12, 31)
    };
}
