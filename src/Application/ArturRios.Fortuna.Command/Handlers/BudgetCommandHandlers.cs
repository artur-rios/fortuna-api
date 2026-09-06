using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateBudgetCommandHandler(
    IValidator<CreateBudgetCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetStore budgets,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateBudgetCommand, BudgetCommandOutput>
{
    public async Task<DataOutput<BudgetCommandOutput?>> HandleAsync(CreateBudgetCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<BudgetCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await BudgetHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<BudgetCommandOutput?>.New.WithError(BudgetMessages.ProfileNotFound);
        }

        var result = await budgets.CreateAsync(
            new BudgetCreation(
                profile.Id,
                command.Amount,
                command.CurrencyCode.Trim().ToUpperInvariant(),
                command.PeriodType,
                command.PeriodStart,
                command.CategoryIds,
                command.IncludeDescendants,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        return BudgetHandler.Resolve(result, BudgetMessages.CreatedSuccessfully);
    }
}

public sealed class UpdateBudgetCommandHandler(
    IValidator<UpdateBudgetCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetUpdater budgets,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateBudgetCommand, BudgetCommandOutput>
{
    public async Task<DataOutput<BudgetCommandOutput?>> HandleAsync(UpdateBudgetCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<BudgetCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await BudgetHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<BudgetCommandOutput?>.New.WithError(BudgetMessages.ProfileNotFound);
        }

        var result = await budgets.UpdateAsync(
            new BudgetUpdate(
                profile.Id,
                command.Id,
                command.Amount,
                command.CurrencyCode.Trim().ToUpperInvariant(),
                command.PeriodType,
                command.PeriodStart,
                command.CategoryIds,
                command.IncludeDescendants,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        return BudgetHandler.Resolve(result, BudgetMessages.UpdatedSuccessfully);
    }
}

public sealed class DeleteBudgetCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetLifecycleStore budgets,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteBudgetCommand, BudgetCommandOutput>
{
    public async Task<DataOutput<BudgetCommandOutput?>> HandleAsync(DeleteBudgetCommand command)
    {
        var now = timeProvider.GetUtcNow();
        var profile = await BudgetHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<BudgetCommandOutput?>.New.WithError(BudgetMessages.ProfileNotFound);
        }

        var result = await budgets.SoftDeleteAsync(
            profile.Id,
            command.Id,
            now,
            DateOnly.FromDateTime(now.UtcDateTime),
            CancellationToken.None);
        return BudgetHandler.Resolve(result, BudgetMessages.DeletedSuccessfully);
    }
}

internal static class BudgetHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<BudgetCommandOutput?> Resolve(
        BudgetMutationResult result,
        string successMessage)
    {
        var output = DataOutput<BudgetCommandOutput?>.New;
        return result.Outcome switch
        {
            BudgetMutationOutcome.Succeeded => output
                .WithData(ToOutput(result.Budget!))
                .WithMessage(successMessage),
            BudgetMutationOutcome.NotFound => output.WithError(BudgetMessages.NotFound),
            BudgetMutationOutcome.CategoryNotFound => output.WithError(
                BudgetMessages.CategoryNotFound),
            BudgetMutationOutcome.CurrencyNotFound => output.WithError(
                BudgetMessages.CurrencyNotSupported),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static BudgetCommandOutput ToOutput(BudgetSnapshot budget) => new()
    {
        Id = budget.Id,
        Amount = budget.Amount,
        CurrencyCode = budget.CurrencyCode,
        PeriodType = budget.PeriodType,
        PeriodStart = budget.PeriodStart,
        IncludeDescendants = budget.IncludeDescendants,
        Categories = budget.Categories.Select(item => new BudgetCategoryCommandOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        CurrentPeriod = new BudgetConsumptionCommandOutput
        {
            PeriodStart = budget.CurrentPeriod.PeriodStart,
            PeriodEnd = budget.CurrentPeriod.PeriodEnd,
            Spent = budget.CurrentPeriod.Spent,
            Remaining = budget.CurrentPeriod.Remaining,
            IsExceeded = budget.CurrentPeriod.IsExceeded,
            Overage = budget.CurrentPeriod.Overage,
            IsFullyConverted = budget.CurrentPeriod.IsFullyConverted
        },
        IsDeleted = budget.IsDeleted,
        CreatedAt = budget.CreatedAt,
        UpdatedAt = budget.UpdatedAt
    };
}
