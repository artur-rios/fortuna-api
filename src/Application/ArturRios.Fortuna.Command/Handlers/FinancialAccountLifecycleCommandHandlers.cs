using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteFinancialAccountCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IFinancialAccountLifecycleStore accounts,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteFinancialAccountCommand, FinancialAccountLifecycleCommandOutput>
{
    public async Task<DataOutput<FinancialAccountLifecycleCommandOutput?>> HandleAsync(
        DeleteFinancialAccountCommand command)
    {
        var profile = await FinancialAccountLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return FinancialAccountLifecycleHandler.ProfileNotFound();
        }

        var result = await accounts.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return FinancialAccountLifecycleHandler.Resolve(
            result,
            FinancialAccountMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreFinancialAccountCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IFinancialAccountLifecycleStore accounts,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreFinancialAccountCommand, FinancialAccountLifecycleCommandOutput>
{
    public async Task<DataOutput<FinancialAccountLifecycleCommandOutput?>> HandleAsync(
        RestoreFinancialAccountCommand command)
    {
        var profile = await FinancialAccountLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return FinancialAccountLifecycleHandler.ProfileNotFound();
        }

        var result = await accounts.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return FinancialAccountLifecycleHandler.Resolve(
            result,
            FinancialAccountMessages.RestoredSuccessfully);
    }
}

public sealed class HardDeleteFinancialAccountCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IFinancialAccountLifecycleStore accounts)
    : ICommandHandlerAsync<HardDeleteFinancialAccountCommand, FinancialAccountLifecycleCommandOutput>
{
    public async Task<DataOutput<FinancialAccountLifecycleCommandOutput?>> HandleAsync(
        HardDeleteFinancialAccountCommand command)
    {
        var profile = await FinancialAccountLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return FinancialAccountLifecycleHandler.ProfileNotFound();
        }

        var result = await accounts.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);
        return FinancialAccountLifecycleHandler.Resolve(
            result,
            FinancialAccountMessages.HardDeletedSuccessfully);
    }
}

internal static class FinancialAccountLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<FinancialAccountLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<FinancialAccountLifecycleCommandOutput?>.New
            .WithError(FinancialAccountMessages.ProfileNotFound);

    public static DataOutput<FinancialAccountLifecycleCommandOutput?> Resolve(
        FinancialAccountLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<FinancialAccountLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            FinancialAccountLifecycleOutcome.Succeeded => output
                .WithData(new FinancialAccountLifecycleCommandOutput { Id = result.Id!.Value })
                .WithMessage(successMessage),
            FinancialAccountLifecycleOutcome.NotFound => output
                .WithError(FinancialAccountMessages.NotFound),
            FinancialAccountLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(FinancialAccountMessages.RestoreRequiresSoftDeletion),
            FinancialAccountLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(FinancialAccountMessages.HardDeleteRequiresSoftDeletion),
            FinancialAccountLifecycleOutcome.HardDeleteHasLiveTransactions => output
                .WithError(FinancialAccountMessages.HardDeleteHasLiveTransactions),
            FinancialAccountLifecycleOutcome.DuplicateName => output
                .WithError(FinancialAccountMessages.DuplicateName),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
