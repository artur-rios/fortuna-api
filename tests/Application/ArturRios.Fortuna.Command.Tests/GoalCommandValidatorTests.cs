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
    public async Task GivenInvalidCurrencyAndResource_WhenUpdated_ThenTheyAreRejected()
    {
        var result = await new UpdateGoalCommandValidator()
            .ValidateAsync(new UpdateGoalCommand
            {
                Name = "Home",
                TargetAmount = 100m,
                CurrencyCode = "REAL",
                TargetDate = new DateOnly(2026, 9, 6),
                AccountIds = [Guid.Empty]
            });

        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.CurrencyInvalid);
        Assert.Contains(result.Errors, item => item.ErrorMessage == GoalMessages.ResourceIdInvalid);
    }

    [UnitFact]
    public async Task GivenPastTargetDate_WhenUpdateValidated_ThenStoreDecidesWhetherItChanged()
    {
        var result = await new UpdateGoalCommandValidator()
            .ValidateAsync(new UpdateGoalCommand
            {
                Name = "Home",
                TargetAmount = 100m,
                CurrencyCode = "BRL",
                TargetDate = new DateOnly(2026, 1, 1),
                AccountIds = [Guid.NewGuid()]
            });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenTodayAsTargetDate_WhenCreated_ThenItIsRejected()
    {
        var command = Valid([Guid.NewGuid()], []);
        command.TargetDate = new DateOnly(2026, 9, 6);

        var result = await new CreateGoalCommandValidator(new FixedTimeProvider(Now))
            .ValidateAsync(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == GoalMessages.TargetDateMustBeFuture);
    }

    [UnitFact]
    public async Task GivenLongLivedValidator_WhenClockPassesTargetDate_ThenCurrentDayIsUsed()
    {
        var clock = new MovableTimeProvider(Now);
        var validator = new CreateGoalCommandValidator(clock);
        var command = Valid([Guid.NewGuid()], []);
        command.TargetDate = new DateOnly(2026, 9, 7);

        var before = await validator.ValidateAsync(command);
        clock.Now = Now.AddDays(2);
        var after = await validator.ValidateAsync(command);

        Assert.True(before.IsValid);
        Assert.Contains(after.Errors, item =>
            item.ErrorMessage == GoalMessages.TargetDateMustBeFuture);
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

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
