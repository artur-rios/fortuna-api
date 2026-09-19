using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class GrantProcessingConsentCommandHandler(
    IValidator<GrantProcessingConsentCommand> validator,
    ICurrentProfileResolver profileResolver,
    IProcessingConsentStore consents,
    ProcessingConsentOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<GrantProcessingConsentCommand, GrantProcessingConsentCommandOutput>
{
    public async Task<DataOutput<GrantProcessingConsentCommandOutput?>> HandleAsync(
        GrantProcessingConsentCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<GrantProcessingConsentCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!ProcessingConsentOptions.TryParsePurpose(command.Purpose, out var purpose))
        {
            return Failure(ProcessingConsentMessages.UnknownPurpose);
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return Failure(ProcessingConsentMessages.ProfileNotFound);
        }

        var currentVersion = options.CurrentVersion(purpose);
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
}

public sealed class WithdrawProcessingConsentCommandHandler(
    IValidator<WithdrawProcessingConsentCommand> validator,
    ICurrentProfileResolver profileResolver,
    IProcessingConsentStore consents,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<WithdrawProcessingConsentCommand, WithdrawProcessingConsentCommandOutput>
{
    public async Task<DataOutput<WithdrawProcessingConsentCommandOutput?>> HandleAsync(
        WithdrawProcessingConsentCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<WithdrawProcessingConsentCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!ProcessingConsentOptions.TryParsePurpose(command.Purpose, out var purpose))
        {
            return Failure(ProcessingConsentMessages.UnknownPurpose);
        }

        var profile = await profileResolver.ResolveAsync();
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
