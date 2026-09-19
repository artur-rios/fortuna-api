using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteTransactionCommandHandler(
    ICurrentProfileResolver profileResolver,
    ITransactionLifecycleStore transactions,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteTransactionCommand, TransactionLifecycleCommandOutput>
{
    public async Task<DataOutput<TransactionLifecycleCommandOutput?>> HandleAsync(
        DeleteTransactionCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return TransactionLifecycleHandler.ProfileNotFound();
        }

        var result = await transactions.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return TransactionLifecycleHandler.Resolve(
            result,
            TransactionMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreTransactionCommandHandler(
    ICurrentProfileResolver profileResolver,
    ITransactionLifecycleStore transactions,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreTransactionCommand, TransactionLifecycleCommandOutput>
{
    public async Task<DataOutput<TransactionLifecycleCommandOutput?>> HandleAsync(
        RestoreTransactionCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return TransactionLifecycleHandler.ProfileNotFound();
        }

        var result = await transactions.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return TransactionLifecycleHandler.Resolve(
            result,
            TransactionMessages.RestoredSuccessfully);
    }
}

public sealed class HardDeleteTransactionCommandHandler(
    ICurrentProfileResolver profileResolver,
    ITransactionLifecycleStore transactions)
    : ICommandHandlerAsync<HardDeleteTransactionCommand, TransactionLifecycleCommandOutput>
{
    public async Task<DataOutput<TransactionLifecycleCommandOutput?>> HandleAsync(
        HardDeleteTransactionCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return TransactionLifecycleHandler.ProfileNotFound();
        }

        var result = await transactions.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);

        return TransactionLifecycleHandler.Resolve(
            result,
            TransactionMessages.HardDeletedSuccessfully);
    }
}

internal static class TransactionLifecycleHandler
{
    public static DataOutput<TransactionLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<TransactionLifecycleCommandOutput?>.New
            .WithError(TransactionMessages.ProfileNotFound);

    public static DataOutput<TransactionLifecycleCommandOutput?> Resolve(
        TransactionLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<TransactionLifecycleCommandOutput?>.New;

        return result.Outcome switch
        {
            TransactionLifecycleOutcome.Succeeded => output
                .WithData(new TransactionLifecycleCommandOutput { Id = result.Id!.Value })
                .WithMessage(successMessage),
            TransactionLifecycleOutcome.NotFound => output.WithError(TransactionMessages.NotFound),
            TransactionLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(TransactionMessages.RestoreRequiresSoftDeletion),
            TransactionLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(TransactionMessages.HardDeleteRequiresSoftDeletion),
            TransactionLifecycleOutcome.SettledStatementFrozen => output
                .WithError(TransactionMessages.SettledStatementFrozen),
            TransactionLifecycleOutcome.AttachmentStorageUnavailable => output
                .WithError(AttachmentMessages.StorageUnavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
