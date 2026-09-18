using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateGoalCommandValidator : AbstractValidator<CreateGoalCommand>
{
    public CreateGoalCommandValidator(TimeProvider timeProvider)
    {
        GoalValidation.Apply(this);
        RuleFor(command => command.TargetDate)
            .Must(date => date > Today(timeProvider))
            .When(command => command.TargetDate != default)
            .WithMessage(GoalMessages.TargetDateMustBeFuture);
    }

    // Read on every validation: validators can outlive the day they were built on.
    private static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}

/// <remarks>
/// An update may keep a target date that has already passed, so whether a changed date lies in
/// the future is decided by the store against the stored goal, not here.
/// </remarks>
public sealed class UpdateGoalCommandValidator : AbstractValidator<UpdateGoalCommand>
{
    public UpdateGoalCommandValidator()
    {
        GoalValidation.Apply(this);
    }
}

internal static class GoalValidation
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : class
    {
        validator.RuleFor(command => Name(command))
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(GoalMessages.NameRequired)
            .MaximumLength(200).WithMessage(GoalMessages.NameTooLong);
        validator.RuleFor(command => TargetAmount(command))
            .GreaterThan(0m).WithMessage(GoalMessages.TargetAmountMustBePositive);
        validator.RuleFor(command => CurrencyCode(command))
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(GoalMessages.CurrencyRequired)
            .Must(code => code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(GoalMessages.CurrencyInvalid);
        validator.RuleFor(command => TargetDate(command))
            .NotEqual(default(DateOnly)).WithMessage(GoalMessages.TargetDateRequired);
        validator.RuleFor(command => AccountIds(command).Count + InvestmentIds(command).Count)
            .GreaterThan(0).WithMessage(GoalMessages.ResourcesRequired);
        validator.RuleForEach(command => AccountIds(command))
            .NotEmpty().WithMessage(GoalMessages.ResourceIdInvalid)
            .OverridePropertyName("AccountIds");
        validator.RuleForEach(command => InvestmentIds(command))
            .NotEmpty().WithMessage(GoalMessages.ResourceIdInvalid)
            .OverridePropertyName("InvestmentIds");
    }

    private static string Name<T>(T command) => command switch
    {
        CreateGoalCommand create => create.Name,
        UpdateGoalCommand update => update.Name,
        _ => string.Empty
    };

    private static decimal TargetAmount<T>(T command) => command switch
    {
        CreateGoalCommand create => create.TargetAmount,
        UpdateGoalCommand update => update.TargetAmount,
        _ => 0m
    };

    private static string CurrencyCode<T>(T command) => command switch
    {
        CreateGoalCommand create => create.CurrencyCode,
        UpdateGoalCommand update => update.CurrencyCode,
        _ => string.Empty
    };

    private static DateOnly TargetDate<T>(T command) => command switch
    {
        CreateGoalCommand create => create.TargetDate,
        UpdateGoalCommand update => update.TargetDate,
        _ => default
    };

    private static IReadOnlyCollection<Guid> AccountIds<T>(T command) => command switch
    {
        CreateGoalCommand create => create.AccountIds,
        UpdateGoalCommand update => update.AccountIds,
        _ => []
    };

    private static IReadOnlyCollection<Guid> InvestmentIds<T>(T command) => command switch
    {
        CreateGoalCommand create => create.InvestmentIds,
        UpdateGoalCommand update => update.InvestmentIds,
        _ => []
    };
}
