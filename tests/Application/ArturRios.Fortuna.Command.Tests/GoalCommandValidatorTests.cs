using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class GoalCommandValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenInvalidGoal_WhenCreated_ThenEveryInvalidFieldIsRejected()
    {
        var result = await new CreateGoalCommandValidator(new FixedTimeProvider(Now))
            .ValidateAsync(new CreateGoalCommand());

        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.NameRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == GoalMessages.TargetAmountMustBePositive);
        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.CurrencyRequired);
        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.TargetDateRequired);
        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.ResourcesRequired);
    }

    [UnitFact]
    public async Task GivenInvalidDateCurrencyAndResource_WhenUpdated_ThenTheyAreRejected()
    {
        var result = await new UpdateGoalCommandValidator(new FixedTimeProvider(Now))
            .ValidateAsync(new UpdateGoalCommand
            {
                Name = "Home",
                TargetAmount = 100m,
                CurrencyCode = "REAL",
                TargetDate = new DateOnly(2026, 9, 6),
                AccountIds = [Guid.Empty]
            });

        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.CurrencyInvalid);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == GoalMessages.TargetDateMustBeFuture);
        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.ResourceIdInvalid);
    }

    [UnitFact]
    public async Task GivenAccountOrInvestment_WhenValidated_ThenBothFormsAreAccepted()
    {
        var account = await new CreateGoalCommandValidator(new FixedTimeProvider(Now))
            .ValidateAsync(Valid([Guid.NewGuid()], []));
        var investment = await new CreateGoalCommandValidator(new FixedTimeProvider(Now))
            .ValidateAsync(Valid([], [Guid.NewGuid()]));

        Assert.True(account.IsValid);
        Assert.True(investment.IsValid);
    }

    private static CreateGoalCommand Valid(
        IReadOnlyCollection<Guid> accounts,
        IReadOnlyCollection<Guid> investments) => new()
        {
            Name = "Home",
            TargetAmount = 100m,
            CurrencyCode = "BRL",
            TargetDate = new DateOnly(2027, 1, 1),
            AccountIds = accounts,
            InvestmentIds = investments
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
