using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class TransactionQueryValidatorTests
{
    [UnitFact]
    public async Task GivenEmptyTransactionId_WhenDetailIsValidated_ThenIdIsRejected()
    {
        var result = await new GetTransactionByIdQueryValidator().ValidateAsync(
            new GetTransactionByIdQuery());

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.TransactionIdRequired);
    }

    [UnitFact]
    public async Task GivenValidTransactionId_WhenDetailIsValidated_ThenValidationSucceeds()
    {
        var result = await new GetTransactionByIdQueryValidator().ValidateAsync(
            new GetTransactionByIdQuery { Id = Guid.NewGuid(), IncludeDeleted = true });

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData(0, 10, "PageNumber must be at least 1.")]
    [InlineData(1, 0, "PageSize must be at least 1.")]
    public async Task GivenInvalidPagination_WhenSearchIsValidated_ThenBoundaryIsRejected(
        int pageNumber,
        int pageSize,
        string expected)
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        Assert.Contains(result.Errors, failure => failure.ErrorMessage == expected);
    }

    [UnitFact]
    public async Task GivenReversedDates_WhenSearchIsValidated_ThenRangeIsRejected()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            From = new DateOnly(2026, 9, 6),
            To = new DateOnly(2026, 9, 5)
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.DateRangeInvalid);
    }

    [UnitTheory]
    [MemberData(nameof(InvalidIdentifiers))]
    public async Task GivenEmptyFilterIdentifier_WhenSearchIsValidated_ThenIdentifierIsRejected(
        SearchTransactionsQuery query,
        string expected)
    {
        var result = await Validator().ValidateAsync(query);

        Assert.Contains(result.Errors, failure => failure.ErrorMessage == expected);
    }

    [UnitFact]
    public async Task GivenUnknownDirection_WhenSearchIsValidated_ThenDirectionIsRejected()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            Direction = (TransactionDirection)999
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.DirectionInvalid);
    }

    [UnitTheory]
    [MemberData(nameof(NegativeAmountBounds))]
    public async Task GivenNegativeAmountBound_WhenSearchIsValidated_ThenBoundIsRejected(
        SearchTransactionsQuery query,
        string expected)
    {
        var result = await Validator().ValidateAsync(query);

        Assert.Contains(result.Errors, failure => failure.ErrorMessage == expected);
    }

    [UnitTheory]
    [MemberData(nameof(ImpreciseAmountBounds))]
    public async Task GivenExcessiveAmountPrecision_WhenSearchIsValidated_ThenPrecisionIsRejected(
        SearchTransactionsQuery query)
    {
        var result = await Validator().ValidateAsync(query);

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.AmountPrecisionInvalid);
    }

    [UnitFact]
    public async Task GivenReversedAmounts_WhenSearchIsValidated_ThenRangeIsRejected()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            MinimumAmount = 2m,
            MaximumAmount = 1m
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.AmountRangeInvalid);
    }

    [UnitFact]
    public async Task GivenOversizedText_WhenSearchIsValidated_ThenTextIsRejected()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            Text = new string('t', 501)
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.SearchTextTooLong);
    }

    [UnitFact]
    public async Task GivenMalformedDisplayCurrency_WhenSearchIsValidated_ThenCurrencyIsRejected()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            DisplayCurrencyCode = "US"
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.DisplayCurrencyInvalid);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("Target")]
    public async Task GivenUnsupportedSort_WhenSearchIsValidated_ThenSortIsRejected(string sortBy)
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            SortBy = sortBy
        });

        Assert.Contains(result.Errors,
            failure => failure.ErrorMessage == TransactionMessages.SortByUnsupported);
    }

    [UnitFact]
    public async Task GivenAllSupportedCriteria_WhenSearchIsValidated_ThenValidationSucceeds()
    {
        var result = await Validator().ValidateAsync(new SearchTransactionsQuery
        {
            From = new DateOnly(2026, 9, 1),
            To = new DateOnly(2026, 9, 5),
            FinancialAccountId = Guid.NewGuid(),
            CreditCardId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            CounterpartyId = Guid.NewGuid(),
            Direction = TransactionDirection.Earning,
            MinimumAmount = 0m,
            MaximumAmount = 999.9999m,
            Text = "salary",
            DisplayCurrencyCode = "BRL",
            SortBy = "UpdatedAt"
        });

        Assert.True(result.IsValid);
    }

    private static SearchTransactionsQueryValidator Validator() => new();

    public static IEnumerable<object[]> InvalidIdentifiers()
    {
        yield return
        [
            new SearchTransactionsQuery { FinancialAccountId = Guid.Empty },
            TransactionMessages.FinancialAccountIdInvalid
        ];
        yield return
        [
            new SearchTransactionsQuery { CreditCardId = Guid.Empty },
            TransactionMessages.CreditCardIdInvalid
        ];
        yield return
        [
            new SearchTransactionsQuery { CategoryId = Guid.Empty },
            TransactionMessages.CategoryFilterIdInvalid
        ];
        yield return
        [
            new SearchTransactionsQuery { TagId = Guid.Empty },
            TransactionMessages.TagIdInvalid
        ];
        yield return
        [
            new SearchTransactionsQuery { CounterpartyId = Guid.Empty },
            TransactionMessages.CounterpartyIdInvalid
        ];
    }

    public static IEnumerable<object[]> NegativeAmountBounds()
    {
        yield return
        [
            new SearchTransactionsQuery { MinimumAmount = -0.01m },
            TransactionMessages.MinimumAmountInvalid
        ];
        yield return
        [
            new SearchTransactionsQuery { MaximumAmount = -0.01m },
            TransactionMessages.MaximumAmountInvalid
        ];
    }

    public static IEnumerable<object[]> ImpreciseAmountBounds()
    {
        yield return [new SearchTransactionsQuery { MinimumAmount = 1.00001m }];
        yield return
        [
            new SearchTransactionsQuery { MaximumAmount = 1_000_000_000_000_000m }
        ];
    }
}
