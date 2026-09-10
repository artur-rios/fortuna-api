using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class GrantProcessingConsentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IProcessingConsentStore consents,
    ProcessingConsentOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<GrantProcessingConsentCommand, GrantProcessingConsentCommandOutput>
{
    public async Task<DataOutput<GrantProcessingConsentCommandOutput?>> HandleAsync(
        GrantProcessingConsentCommand command)
    {
        if (!ProcessingConsentOptions.TryParsePurpose(command.Purpose, out var purpose))
        {
            return Failure(ProcessingConsentMessages.UnknownPurpose);
        }
        if (string.IsNullOrWhiteSpace(command.Version))
        {
            return Failure(ProcessingConsentMessages.VersionRequired);
        }
        var currentVersion = options.CurrentVersion(purpose);
        if (!string.Equals(command.Version.Trim(), currentVersion, StringComparison.Ordinal))
        {
            return Failure(ProcessingConsentMessages.VersionNotCurrent);
        }
        var profile = await ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return Failure(ProcessingConsentMessages.ProfileNotFound);
        }
        var consent = await consents.GrantAsync(
            profile.Id,
            purpose,
            currentVersion,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return DataOutput<GrantProcessingConsentCommandOutput?>.New
            .WithData(new GrantProcessingConsentCommandOutput
            {
                Id = consent.Id,
                Purpose = ProcessingConsentOptions.Name(consent.Purpose),
                Version = consent.Version,
                GrantedAt = consent.GrantedAt,
                IsCurrent = true
            })
            .WithMessage(ProcessingConsentMessages.GrantedSuccessfully);
    }

    private static DataOutput<GrantProcessingConsentCommandOutput?> Failure(string message) =>
        DataOutput<GrantProcessingConsentCommandOutput?>.New.WithError(message);

    internal static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}

public sealed class WithdrawProcessingConsentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IProcessingConsentStore consents,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<WithdrawProcessingConsentCommand, WithdrawProcessingConsentCommandOutput>
{
    public async Task<DataOutput<WithdrawProcessingConsentCommandOutput?>> HandleAsync(
        WithdrawProcessingConsentCommand command)
    {
        if (!ProcessingConsentOptions.TryParsePurpose(command.Purpose, out var purpose))
        {
            return Failure(ProcessingConsentMessages.UnknownPurpose);
        }
        var profile = await GrantProcessingConsentCommandHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return Failure(ProcessingConsentMessages.ProfileNotFound);
        }
        var result = await consents.WithdrawAsync(
            profile.Id,
            purpose,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        if (!result.Found)
        {
            return Failure(ProcessingConsentMessages.NotFound);
        }
        return DataOutput<WithdrawProcessingConsentCommandOutput?>.New
            .WithData(new WithdrawProcessingConsentCommandOutput
            {
                Purpose = ProcessingConsentOptions.Name(purpose),
                RevokedConnections = result.RevokedConnections,
                StoppedSynchronizations = result.StoppedSynchronizations
            })
            .WithMessage(ProcessingConsentMessages.WithdrawnSuccessfully);
    }

    private static DataOutput<WithdrawProcessingConsentCommandOutput?> Failure(string message) =>
        DataOutput<WithdrawProcessingConsentCommandOutput?>.New.WithError(message);
}
