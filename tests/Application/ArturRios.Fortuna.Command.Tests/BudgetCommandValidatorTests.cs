using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class BudgetCommandValidatorTests
{
    [UnitFact]
    public async Task GivenInvalidBudget_WhenCreated_ThenEveryInvalidFieldIsRejected()
    {
        var result = await new CreateBudgetCommandValidator().ValidateAsync(
            new CreateBudgetCommand
            {
                Amount = 0m,
                CurrencyCode = string.Empty,
                PeriodType = (BudgetPeriodType)99,
                PeriodStart = default,
                CategoryIds = []
            });

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.AmountMustBePositive);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.CurrencyRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.PeriodTypeInvalid);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.PeriodStartRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.CategoriesRequired);
    }

    [UnitFact]
    public async Task GivenInvalidCategoryAndCurrency_WhenUpdated_ThenTheyAreRejected()
    {
        var result = await new UpdateBudgetCommandValidator().ValidateAsync(
            new UpdateBudgetCommand
            {
                Amount = 100m,
                CurrencyCode = "REAL",
                PeriodType = BudgetPeriodType.Monthly,
                PeriodStart = new DateOnly(2026, 9, 1),
                CategoryIds = [Guid.Empty]
            });

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.CurrencyInvalid);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == BudgetMessages.CategoryIdInvalid);
    }

    [UnitFact]
    public async Task GivenValidBudgets_WhenValidated_ThenTheyAreAccepted()
    {
        var categoryId = Guid.NewGuid();
        var create = await new CreateBudgetCommandValidator().ValidateAsync(
            new CreateBudgetCommand
            {
                Amount = 100m,
                CurrencyCode = "BRL",
                PeriodType = BudgetPeriodType.Monthly,
                PeriodStart = new DateOnly(2026, 9, 1),
                CategoryIds = [categoryId]
            });
        var update = await new UpdateBudgetCommandValidator().ValidateAsync(
            new UpdateBudgetCommand
            {
                Amount = 200m,
                CurrencyCode = "USD",
                PeriodType = BudgetPeriodType.Yearly,
                PeriodStart = new DateOnly(2026, 1, 1),
                CategoryIds = [categoryId],
                IncludeDescendants = false
            });

        Assert.True(create.IsValid);
        Assert.True(update.IsValid);
    }
}
