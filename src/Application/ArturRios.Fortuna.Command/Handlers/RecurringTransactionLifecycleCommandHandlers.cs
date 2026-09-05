using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteRecurringTransactionCommandHandler(
    IRequestActorAccessor actors,
    IUserProfileReader profiles,
    IRecurringTransactionLifecycleStore rules,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteRecurringTransactionCommand, RecurringTransactionLifecycleCommandOutput>
{
    public async Task<DataOutput<RecurringTransactionLifecycleCommandOutput?>> HandleAsync(
        DeleteRecurringTransactionCommand command)
    {
        var output = DataOutput<RecurringTransactionLifecycleCommandOutput?>.New;
        var profile = await RecurringTransactionHandler.ResolveProfileAsync(actors.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        }

        var result = await rules.SoftDeleteAsync(
            profile.Id, command.Id, timeProvider.GetUtcNow(), CancellationToken.None);
        return result.Outcome switch
        {
            RecurringTransactionLifecycleOutcome.Succeeded => output
                .WithData(new RecurringTransactionLifecycleCommandOutput
                {
                    Id = result.Id!.Value,
                    MaterializedOccurrencesChanged = false
                })
                .WithMessage(RecurringTransactionMessages.DeletedSuccessfully),
            RecurringTransactionLifecycleOutcome.NotFound =>
                output.WithError(RecurringTransactionMessages.NotFound),
            _ => throw new InvalidOperationException("Unknown recurring transaction lifecycle outcome.")
        };
    }
}

internal static class RecurringTransactionHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
