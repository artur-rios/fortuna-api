using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateCounterpartyCommandHandler(
    ICurrentProfileResolver profileResolver,
    ICounterpartyStore counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        CreateCounterpartyCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
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
    ICurrentProfileResolver profileResolver,
    ICounterpartyUpdater counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        UpdateCounterpartyCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
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
    ICurrentProfileResolver profileResolver,
    ICounterpartyLifecycleStore counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteCounterpartyCommand, CounterpartyCommandOutput>
{
    public async Task<DataOutput<CounterpartyCommandOutput?>> HandleAsync(
        DeleteCounterpartyCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
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
    ICurrentProfileResolver profileResolver,
    ICounterpartyMerger counterparties,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<MergeCounterpartiesCommand, CounterpartyMergeCommandOutput>
{
    public async Task<DataOutput<CounterpartyMergeCommandOutput?>> HandleAsync(
        MergeCounterpartiesCommand command)
    {
        var output = DataOutput<CounterpartyMergeCommandOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
