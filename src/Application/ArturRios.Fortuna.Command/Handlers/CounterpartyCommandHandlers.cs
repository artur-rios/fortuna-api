using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateCounterpartyCommandHandler(
    IValidator<CreateCounterpartyCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyStore counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        CreateCounterpartyCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<CounterpartyCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await CounterpartyHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<CounterpartyCommandOutput?>.New.WithError(
                CounterpartyMessages.ProfileNotFound);
        }

        var result = await counterparties.CreateAsync(
            new CounterpartyCreation(profile.Id, command.Name, timeProvider.GetUtcNow()),
            CancellationToken.None);
        return CounterpartyHandler.Resolve(
            result.Counterparty,
            result.Outcome,
            result.Reused
                ? CounterpartyMessages.ReusedSuccessfully
                : CounterpartyMessages.CreatedSuccessfully,
            result.Reused);
    }
}

public sealed class UpdateCounterpartyCommandHandler(
    IValidator<UpdateCounterpartyCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyUpdater counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        UpdateCounterpartyCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<CounterpartyCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await CounterpartyHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<CounterpartyCommandOutput?>.New.WithError(
                CounterpartyMessages.ProfileNotFound);
        }

        var result = await counterparties.UpdateAsync(
            new CounterpartyUpdate(
                profile.Id,
                command.Id,
                command.Name,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        return CounterpartyHandler.Resolve(
            result.Counterparty,
            result.Outcome,
            CounterpartyMessages.UpdatedSuccessfully);
    }
}

public sealed class DeleteCounterpartyCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyLifecycleStore counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        DeleteCounterpartyCommand command)
    {
        var profile = await CounterpartyHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<CounterpartyCommandOutput?>.New.WithError(
                CounterpartyMessages.ProfileNotFound);
        }

        var result = await counterparties.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return CounterpartyHandler.Resolve(
            result.Counterparty,
            result.Outcome,
            CounterpartyMessages.DeletedSuccessfully);
    }
}

public sealed class MergeCounterpartiesCommandHandler(
    IValidator<MergeCounterpartiesCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyMerger counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<MergeCounterpartiesCommand, CounterpartyMergeCommandOutput>
{
    public async Task<DataOutput<CounterpartyMergeCommandOutput?>> HandleAsync(
        MergeCounterpartiesCommand command)
    {
        var output = DataOutput<CounterpartyMergeCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await CounterpartyHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(CounterpartyMessages.ProfileNotFound);
        }

        var result = await counterparties.MergeAsync(
            new CounterpartyMerge(
                profile.Id,
                command.Id,
                command.TargetId,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        return result.Outcome switch
        {
            CounterpartyMergeOutcome.Succeeded => output
                .WithData(new CounterpartyMergeCommandOutput
                {
                    Id = result.SourceId!.Value,
                    SourceId = result.SourceId!.Value,
                    TargetId = result.TargetId!.Value,
                    ReassignedTransactionCount = result.ReassignedTransactionCount
                })
                .WithMessage(CounterpartyMessages.MergedSuccessfully),
            CounterpartyMergeOutcome.NotFound => output.WithError(CounterpartyMessages.NotFound),
            CounterpartyMergeOutcome.SameCounterparty => output.WithError(
                CounterpartyMessages.SameCounterparty),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}

internal static class CounterpartyHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<CounterpartyCommandOutput?> Resolve(
        CounterpartySnapshot? counterparty,
        CounterpartyMutationOutcome outcome,
        string successMessage,
        bool reused = false)
    {
        var output = DataOutput<CounterpartyCommandOutput?>.New;
        return outcome switch
        {
            CounterpartyMutationOutcome.Succeeded => output
                .WithData(new CounterpartyCommandOutput
                {
                    Id = counterparty!.Id,
                    Name = counterparty.Name,
                    IsDeleted = counterparty.IsDeleted,
                    Reused = reused,
                    CreatedAt = counterparty.CreatedAt,
                    UpdatedAt = counterparty.UpdatedAt
                })
                .WithMessage(successMessage),
            CounterpartyMutationOutcome.NotFound => output.WithError(
                CounterpartyMessages.NotFound),
            CounterpartyMutationOutcome.DuplicateName => output.WithError(
                CounterpartyMessages.DuplicateName),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }
}
