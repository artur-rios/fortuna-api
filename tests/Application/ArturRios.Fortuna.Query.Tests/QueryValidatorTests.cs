using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class QueryValidatorTests
{
    public static TheoryData<string, string> EmptyIdentifierCases => new()
    {
        { nameof(GetCategoryByIdQuery), CategoryMessages.NotFound },
        { nameof(GetBudgetByIdQuery), BudgetMessages.NotFound },
        { nameof(GetBudgetConsumptionQuery), BudgetMessages.NotFound },
        { nameof(GetGoalByIdQuery), GoalMessages.NotFound },
        { nameof(GetGoalProgressQuery), GoalMessages.NotFound },
        { nameof(GetFinancialAccountByIdQuery), FinancialAccountMessages.NotFound },
        { nameof(GetFinancialAccountBalanceQuery), FinancialAccountMessages.NotFound },
        { nameof(GetCreditCardByIdQuery), CreditCardMessages.NotFound },
        { nameof(GetCreditCardStatementByIdQuery), CreditCardStatementMessages.NotFound },
        { nameof(GetConnectionByIdQuery), ConnectionMessages.NotFound },
        { nameof(GetImportJobByIdQuery), ImportJobMessages.NotFound },
        { nameof(SuggestCounterpartyCategoryQuery), CounterpartyMessages.NotFound },
        { nameof(GetPersonalDataExportQuery), PersonalDataExportMessages.NotFound }
    };

    [UnitTheory]
    [MemberData(nameof(EmptyIdentifierCases))]
    public async Task GivenEmptyIdentifier_WhenValidated_ThenNotFoundIsReported(
        string queryName,
        string expected)
    {
        var errors = await EmptyIdentifierErrorsAsync(queryName);

        Assert.Equal([expected], errors);
    }

    [UnitTheory]
    [InlineData("BRL", true)]
    [InlineData(" brl ", true)]
    [InlineData("BR", false)]
    [InlineData("B1L", false)]
    [InlineData("", false)]
    public async Task GivenCurrencyCode_WhenValidated_ThenTrimmedThreeLettersAreRequired(
        string code,
        bool isValid)
    {
        var result = await new GetCurrencyByCodeQueryValidator()
            .ValidateAsync(new GetCurrencyByCodeQuery { Code = code });

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
        {
            Assert.Equal(CurrencyMessages.CurrencyNotFound, Assert.Single(result.Errors).ErrorMessage);
        }
    }

    [UnitTheory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2031, 1, 1, true)]
    [InlineData(2100, 12, 31, true)]
    [InlineData(2101, 1, 1, false)]
    public async Task GivenAsOfDate_WhenValidated_ThenItMustBeWithinBounds(
        int year,
        int month,
        int day,
        bool isValid)
    {
        var asOf = new DateOnly(year, month, day);

        var balance = await new GetFinancialAccountBalanceQueryValidator().ValidateAsync(
            new GetFinancialAccountBalanceQuery { Id = Guid.NewGuid(), AsOf = asOf });
        var position = await new GetNetPositionQueryValidator().ValidateAsync(
            new GetNetPositionQuery { AsOf = asOf });

        Assert.Equal(isValid, balance.IsValid);
        Assert.Equal(isValid, position.IsValid);
        if (!isValid)
        {
            Assert.Equal(FinancialAccountMessages.AsOfOutOfRange,
                Assert.Single(balance.Errors).ErrorMessage);
            Assert.Equal(NetPositionMessages.AsOfOutOfRange,
                Assert.Single(position.Errors).ErrorMessage);
        }
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" usd ")]
    public async Task GivenMissingBlankOrPaddedDisplayCurrency_WhenValidated_ThenItIsAccepted(
        string? code)
    {
        var search = await new SearchTransactionsQueryValidator().ValidateAsync(
            new SearchTransactionsQuery { DisplayCurrencyCode = code });
        var position = await new GetNetPositionQueryValidator().ValidateAsync(
            new GetNetPositionQuery { DisplayCurrencyCode = code });
        var investments = await new ListInvestmentsQueryValidator().ValidateAsync(
            new ListInvestmentsQuery { DisplayCurrencyCode = code });

        Assert.True(search.IsValid);
        Assert.True(position.IsValid);
        Assert.True(investments.IsValid);
    }

    [UnitFact]
    public async Task GivenPaddedButShortDisplayCurrency_WhenSearchValidated_ThenItIsRejected()
    {
        var result = await new SearchTransactionsQueryValidator().ValidateAsync(
            new SearchTransactionsQuery { DisplayCurrencyCode = " US " });

        Assert.Equal(TransactionMessages.DisplayCurrencyInvalid,
            Assert.Single(result.Errors).ErrorMessage);
    }

    [UnitFact]
    public async Task GivenInvalidPages_WhenListsValidated_ThenPageErrorsAreReported()
    {
        var budgets = await new ListBudgetsQueryValidator().ValidateAsync(
            new ListBudgetsQuery { PageNumber = 0, PageSize = 0 });
        var goals = await new ListGoalsQueryValidator().ValidateAsync(
            new ListGoalsQuery { PageNumber = 0, PageSize = 0 });
        var counterparties = await new ListCounterpartiesQueryValidator().ValidateAsync(
            new ListCounterpartiesQuery { PageNumber = 0, PageSize = 0 });

        Assert.Equal([BudgetMessages.InvalidPageNumber, BudgetMessages.InvalidPageSize],
            budgets.Errors.Select(error => error.ErrorMessage));
        Assert.Equal([GoalMessages.InvalidPageNumber, GoalMessages.InvalidPageSize],
            goals.Errors.Select(error => error.ErrorMessage));
        Assert.Equal(
            [CounterpartyMessages.InvalidPageNumber, CounterpartyMessages.InvalidPageSize],
            counterparties.Errors.Select(error => error.ErrorMessage));
    }

    private static async Task<IReadOnlyCollection<string>> EmptyIdentifierErrorsAsync(
        string queryName) => queryName switch
        {
            nameof(GetCategoryByIdQuery) => await ErrorsAsync(
                new GetCategoryByIdQueryValidator(), new GetCategoryByIdQuery()),
            nameof(GetBudgetByIdQuery) => await ErrorsAsync(
                new GetBudgetByIdQueryValidator(), new GetBudgetByIdQuery()),
            nameof(GetBudgetConsumptionQuery) => await ErrorsAsync(
                new GetBudgetConsumptionQueryValidator(), new GetBudgetConsumptionQuery()),
            nameof(GetGoalByIdQuery) => await ErrorsAsync(
                new GetGoalByIdQueryValidator(), new GetGoalByIdQuery()),
            nameof(GetGoalProgressQuery) => await ErrorsAsync(
                new GetGoalProgressQueryValidator(), new GetGoalProgressQuery()),
            nameof(GetFinancialAccountByIdQuery) => await ErrorsAsync(
                new GetFinancialAccountByIdQueryValidator(), new GetFinancialAccountByIdQuery()),
            nameof(GetFinancialAccountBalanceQuery) => await ErrorsAsync(
                new GetFinancialAccountBalanceQueryValidator(),
                new GetFinancialAccountBalanceQuery()),
            nameof(GetCreditCardByIdQuery) => await ErrorsAsync(
                new GetCreditCardByIdQueryValidator(), new GetCreditCardByIdQuery()),
            nameof(GetCreditCardStatementByIdQuery) => await ErrorsAsync(
                new GetCreditCardStatementByIdQueryValidator(),
                new GetCreditCardStatementByIdQuery()),
            nameof(GetConnectionByIdQuery) => await ErrorsAsync(
                new GetConnectionByIdQueryValidator(), new GetConnectionByIdQuery()),
            nameof(GetImportJobByIdQuery) => await ErrorsAsync(
                new GetImportJobByIdQueryValidator(), new GetImportJobByIdQuery()),
            nameof(SuggestCounterpartyCategoryQuery) => await ErrorsAsync(
                new SuggestCounterpartyCategoryQueryValidator(),
                new SuggestCounterpartyCategoryQuery()),
            _ => await ErrorsAsync(
                new GetPersonalDataExportQueryValidator(), new GetPersonalDataExportQuery())
        };

    private static async Task<IReadOnlyCollection<string>> ErrorsAsync<T>(
        IValidator<T> validator,
        T query) => (await validator.ValidateAsync(query)).Errors
        .Select(error => error.ErrorMessage)
        .ToArray();
}
