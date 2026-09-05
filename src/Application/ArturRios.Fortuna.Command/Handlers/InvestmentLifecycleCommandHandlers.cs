using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteInvestmentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInvestmentLifecycleStore investments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteInvestmentCommand, InvestmentLifecycleCommandOutput>
{
    public async Task<DataOutput<InvestmentLifecycleCommandOutput?>> HandleAsync(
        DeleteInvestmentCommand command)
    {
        var profile = await InvestmentLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return InvestmentLifecycleHandler.ProfileNotFound();
        }

        var result = await investments.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return InvestmentLifecycleHandler.Resolve(result, InvestmentMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreInvestmentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInvestmentLifecycleStore investments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreInvestmentCommand, InvestmentLifecycleCommandOutput>
{
    public async Task<DataOutput<InvestmentLifecycleCommandOutput?>> HandleAsync(
        RestoreInvestmentCommand command)
    {
        var profile = await InvestmentLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return InvestmentLifecycleHandler.ProfileNotFound();
        }

        var result = await investments.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return InvestmentLifecycleHandler.Resolve(result, InvestmentMessages.RestoredSuccessfully);
    }
}

public sealed class HardDeleteInvestmentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInvestmentLifecycleStore investments)
    : ICommandHandlerAsync<HardDeleteInvestmentCommand, InvestmentLifecycleCommandOutput>
{
    public async Task<DataOutput<InvestmentLifecycleCommandOutput?>> HandleAsync(
        HardDeleteInvestmentCommand command)
    {
        var profile = await InvestmentLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return InvestmentLifecycleHandler.ProfileNotFound();
        }

        var result = await investments.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);
        return InvestmentLifecycleHandler.Resolve(
            result,
            InvestmentMessages.HardDeletedSuccessfully);
    }
}

internal static class InvestmentLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<InvestmentLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<InvestmentLifecycleCommandOutput?>.New
            .WithError(InvestmentMessages.ProfileNotFound);

    public static DataOutput<InvestmentLifecycleCommandOutput?> Resolve(
        InvestmentLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<InvestmentLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            InvestmentLifecycleOutcome.Succeeded => output
                .WithData(new InvestmentLifecycleCommandOutput { Id = result.Id!.Value })
                .WithMessage(successMessage),
            InvestmentLifecycleOutcome.NotFound => output.WithError(InvestmentMessages.NotFound),
            InvestmentLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(InvestmentMessages.RestoreRequiresSoftDeletion),
            InvestmentLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(InvestmentMessages.HardDeleteRequiresSoftDeletion),
            InvestmentLifecycleOutcome.HardDeleteHasLiveGoal => output
                .WithError(InvestmentMessages.HardDeleteHasLiveGoal)
                .WithMessage(InvestmentMessages.ReferencingGoal(result.ReferencingGoal!)),
            InvestmentLifecycleOutcome.DuplicateInstrument => output
                .WithError(InvestmentMessages.DuplicateInstrument),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
