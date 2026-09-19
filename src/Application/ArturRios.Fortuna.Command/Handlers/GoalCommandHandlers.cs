using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateGoalCommandHandler(
    IValidator<CreateGoalCommand> validator,
    ICurrentProfileResolver profileResolver,
    IGoalStore goals,
    TimeProvider timeProvider) : ICommandHandlerAsync<CreateGoalCommand, GoalCommandOutput>
{
    public async Task<DataOutput<GoalCommandOutput?>> HandleAsync(CreateGoalCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<GoalCommandOutput?>.New.WithErrors(
                validation.Errors.Select(item => item.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<GoalCommandOutput?>.New.WithError(GoalMessages.ProfileNotFound);
        }

        var result = await goals.CreateAsync(new GoalCreation(
            profile.Id,
            command.Name.Trim(),
            command.TargetAmount,
            command.CurrencyCode.Trim().ToUpperInvariant(),
            command.TargetDate,
            command.AccountIds,
            command.InvestmentIds,
            timeProvider.GetUtcNow()), CancellationToken.None);

        return GoalHandler.Resolve(result, GoalMessages.CreatedSuccessfully);
    }
}

public sealed class UpdateGoalCommandHandler(
    IValidator<UpdateGoalCommand> validator,
    ICurrentProfileResolver profileResolver,
    IGoalUpdater goals,
    TimeProvider timeProvider) : ICommandHandlerAsync<UpdateGoalCommand, GoalCommandOutput>
{
    public async Task<DataOutput<GoalCommandOutput?>> HandleAsync(UpdateGoalCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<GoalCommandOutput?>.New.WithErrors(
                validation.Errors.Select(item => item.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<GoalCommandOutput?>.New.WithError(GoalMessages.ProfileNotFound);
        }

        var result = await goals.UpdateAsync(new GoalUpdate(
            profile.Id,
            command.Id,
            command.Name.Trim(),
            command.TargetAmount,
            command.CurrencyCode.Trim().ToUpperInvariant(),
            command.TargetDate,
            command.AccountIds,
            command.InvestmentIds,
            timeProvider.GetUtcNow()), CancellationToken.None);

        return GoalHandler.Resolve(result, GoalMessages.UpdatedSuccessfully);
    }
}

public sealed class DeleteGoalCommandHandler(
    ICurrentProfileResolver profileResolver,
    IGoalLifecycleStore goals,
    TimeProvider timeProvider) : ICommandHandlerAsync<DeleteGoalCommand, GoalCommandOutput>
{
    public async Task<DataOutput<GoalCommandOutput?>> HandleAsync(DeleteGoalCommand command)
    {
        var now = timeProvider.GetUtcNow();
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<GoalCommandOutput?>.New.WithError(GoalMessages.ProfileNotFound);
        }

        var result = await goals.SoftDeleteAsync(
            profile.Id,
            command.Id,
            now,
            DateOnly.FromDateTime(now.UtcDateTime),
            CancellationToken.None);

        return GoalHandler.Resolve(result, GoalMessages.DeletedSuccessfully);
    }
}

internal static class GoalHandler
{
    public static DataOutput<GoalCommandOutput?> Resolve(
        GoalMutationResult result,
        string message)
    {
        var output = DataOutput<GoalCommandOutput?>.New;

        return result.Outcome switch
        {
            GoalMutationOutcome.Succeeded => output.WithData(ToOutput(result.Goal!))
                .WithMessage(message),
            GoalMutationOutcome.NotFound => output.WithError(GoalMessages.NotFound),
            GoalMutationOutcome.ResourceNotFound => output.WithError(GoalMessages.ResourceNotFound),
            GoalMutationOutcome.CurrencyNotFound => output.WithError(
                GoalMessages.CurrencyNotSupported),
            GoalMutationOutcome.TargetDateNotFuture => output.WithError(
                GoalMessages.TargetDateMustBeFuture),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static GoalCommandOutput ToOutput(GoalSnapshot goal) => new()
    {
        Id = goal.Id,
        Name = goal.Name,
        TargetAmount = goal.TargetAmount,
        CurrencyCode = goal.CurrencyCode,
        TargetDate = goal.TargetDate,
        Accounts = goal.Accounts.Select(item => new GoalResourceCommandOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        Investments = goal.Investments.Select(item => new GoalResourceCommandOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        CurrentProgress = new GoalProgressCommandOutput
        {
            CurrentAmount = goal.CurrentProgress.CurrentAmount,
            Remaining = goal.CurrentProgress.Remaining,
            ProportionReached = goal.CurrentProgress.ProportionReached,
            IsReached = goal.CurrentProgress.IsReached,
            IsFullyConverted = goal.CurrentProgress.IsFullyConverted
        },
        IsDeleted = goal.IsDeleted,
        CreatedAt = goal.CreatedAt,
        UpdatedAt = goal.UpdatedAt
    };
}
