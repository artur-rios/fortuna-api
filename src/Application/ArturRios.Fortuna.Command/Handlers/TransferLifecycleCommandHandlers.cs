using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteTransferCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransferLifecycleStore transfers,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteTransferCommand, TransferLifecycleCommandOutput>
{
    public async Task<DataOutput<TransferLifecycleCommandOutput?>> HandleAsync(
        DeleteTransferCommand command)
    {
        var profile = await TransferLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return TransferLifecycleHandler.ProfileNotFound();
        }

        var result = await transfers.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return TransferLifecycleHandler.Resolve(result, TransferMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreTransferCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransferLifecycleStore transfers,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreTransferCommand, TransferLifecycleCommandOutput>
{
    public async Task<DataOutput<TransferLifecycleCommandOutput?>> HandleAsync(
        RestoreTransferCommand command)
    {
        var profile = await TransferLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return TransferLifecycleHandler.ProfileNotFound();
        }

        var result = await transfers.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return TransferLifecycleHandler.Resolve(result, TransferMessages.RestoredSuccessfully);
    }
}

internal static class TransferLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<TransferLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<TransferLifecycleCommandOutput?>.New
            .WithError(TransferMessages.ProfileNotFound);

    public static DataOutput<TransferLifecycleCommandOutput?> Resolve(
        TransferLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<TransferLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            TransferLifecycleOutcome.Succeeded => output
                .WithData(new TransferLifecycleCommandOutput { Id = result.Id!.Value })
                .WithMessage(successMessage),
            TransferLifecycleOutcome.NotFound => output.WithError(TransferMessages.NotFound),
            TransferLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(TransferMessages.RestoreRequiresSoftDeletion),
            TransferLifecycleOutcome.SettledStatementFrozen => output
                .WithError(TransferMessages.SettledStatementFrozen),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
