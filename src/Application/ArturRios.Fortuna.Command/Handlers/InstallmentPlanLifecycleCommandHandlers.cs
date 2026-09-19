using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteInstallmentPlanCommandHandler(
    ICurrentProfileResolver profileResolver,
    IInstallmentPlanLifecycleStore plans,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteInstallmentPlanCommand, InstallmentPlanLifecycleCommandOutput>
{
    public async Task<DataOutput<InstallmentPlanLifecycleCommandOutput?>> HandleAsync(
        DeleteInstallmentPlanCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return InstallmentPlanHandler.ProfileNotFound();
        }

        var result = await plans.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return InstallmentPlanHandler.Resolve(
            result,
            InstallmentPlanMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreInstallmentPlanCommandHandler(
    ICurrentProfileResolver profileResolver,
    IInstallmentPlanLifecycleStore plans,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreInstallmentPlanCommand, InstallmentPlanLifecycleCommandOutput>
{
    public async Task<DataOutput<InstallmentPlanLifecycleCommandOutput?>> HandleAsync(
        RestoreInstallmentPlanCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return InstallmentPlanHandler.ProfileNotFound();
        }

        var result = await plans.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return InstallmentPlanHandler.Resolve(
            result,
            InstallmentPlanMessages.RestoredSuccessfully);
    }
}

internal static class InstallmentPlanHandler
{
    public static DataOutput<InstallmentPlanLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<InstallmentPlanLifecycleCommandOutput?>.New
            .WithError(InstallmentPlanMessages.ProfileNotFound);

    public static DataOutput<InstallmentPlanLifecycleCommandOutput?> Resolve(
        InstallmentPlanLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<InstallmentPlanLifecycleCommandOutput?>.New;

        return result.Outcome switch
        {
            InstallmentPlanLifecycleOutcome.Succeeded => output
                .WithData(new InstallmentPlanLifecycleCommandOutput { Id = result.Id!.Value })
                .WithMessage(successMessage),
            InstallmentPlanLifecycleOutcome.NotFound =>
                output.WithError(InstallmentPlanMessages.NotFound),
            InstallmentPlanLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(InstallmentPlanMessages.RestoreRequiresSoftDeletion),
            InstallmentPlanLifecycleOutcome.SettledStatementFrozen => output
                .WithError(InstallmentPlanMessages.SettledStatementFrozen),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
