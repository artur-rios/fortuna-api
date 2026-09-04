using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteCreditCardCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICreditCardLifecycleStore cards,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteCreditCardCommand, CreditCardLifecycleCommandOutput>
{
    public async Task<DataOutput<CreditCardLifecycleCommandOutput?>> HandleAsync(
        DeleteCreditCardCommand command)
    {
        var profile = await CreditCardLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CreditCardLifecycleHandler.ProfileNotFound();
        }

        var result = await cards.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return CreditCardLifecycleHandler.Resolve(result, CreditCardMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreCreditCardCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICreditCardLifecycleStore cards,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreCreditCardCommand, CreditCardLifecycleCommandOutput>
{
    public async Task<DataOutput<CreditCardLifecycleCommandOutput?>> HandleAsync(
        RestoreCreditCardCommand command)
    {
        var profile = await CreditCardLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CreditCardLifecycleHandler.ProfileNotFound();
        }

        var result = await cards.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return CreditCardLifecycleHandler.Resolve(result, CreditCardMessages.RestoredSuccessfully);
    }
}

public sealed class HardDeleteCreditCardCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICreditCardLifecycleStore cards)
    : ICommandHandlerAsync<HardDeleteCreditCardCommand, CreditCardLifecycleCommandOutput>
{
    public async Task<DataOutput<CreditCardLifecycleCommandOutput?>> HandleAsync(
        HardDeleteCreditCardCommand command)
    {
        var profile = await CreditCardLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CreditCardLifecycleHandler.ProfileNotFound();
        }

        var result = await cards.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);
        return CreditCardLifecycleHandler.Resolve(result, CreditCardMessages.HardDeletedSuccessfully);
    }
}

internal static class CreditCardLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<CreditCardLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<CreditCardLifecycleCommandOutput?>.New
            .WithError(CreditCardMessages.ProfileNotFound);

    public static DataOutput<CreditCardLifecycleCommandOutput?> Resolve(
        CreditCardLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<CreditCardLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            CreditCardLifecycleOutcome.Succeeded => output
                .WithData(new CreditCardLifecycleCommandOutput
                {
                    Id = result.Id!.Value,
                    CurrencyCode = result.CurrencyCode!,
                    OutstandingAmount = result.OutstandingAmount
                })
                .WithMessage(successMessage),
            CreditCardLifecycleOutcome.NotFound => output.WithError(CreditCardMessages.NotFound),
            CreditCardLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(CreditCardMessages.RestoreRequiresSoftDeletion),
            CreditCardLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(CreditCardMessages.HardDeleteRequiresSoftDeletion),
            CreditCardLifecycleOutcome.HardDeleteHasLiveTransactions => output
                .WithError(CreditCardMessages.HardDeleteHasLiveTransactions),
            CreditCardLifecycleOutcome.DuplicateName => output
                .WithError(CreditCardMessages.DuplicateName),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
