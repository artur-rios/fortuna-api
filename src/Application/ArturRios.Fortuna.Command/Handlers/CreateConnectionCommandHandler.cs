using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateConnectionCommandHandler(
    ICurrentProfileResolver profileResolver,
    IConnectionStore connections,
    IPluggyConnectionGateway pluggy,
    IConnectionAccessTokenProtector protector,
    TimeProvider timeProvider,
    IProcessingConsentReader consents,
    ProcessingConsentOptions consentOptions)
    : ICommandHandlerAsync<CreateConnectionCommand, CreateConnectionCommandOutput>
{
    public async Task<DataOutput<CreateConnectionCommandOutput?>> HandleAsync(
        CreateConnectionCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<CreateConnectionCommandOutput?>.New.WithError(
                ConnectionMessages.ProfileNotFound);
        }

        if (!await consents.IsCurrentAsync(
                profile.Id,
                ProcessingConsentPurpose.ExternalDataProcessing,
                consentOptions.ExternalDataProcessingVersion,
                CancellationToken.None))
        {
            return DataOutput<CreateConnectionCommandOutput?>.New.WithError(
                ProcessingConsentMessages.ExternalDataProcessingRequired);
        }

        if (!Guid.TryParse(command.ExternalReference?.Trim(), out var itemId))
        {
            return DataOutput<CreateConnectionCommandOutput?>.New.WithError(
                ConnectionMessages.ExternalReferenceInvalid);
        }

        var externalReference = itemId.ToString();
        var verified = await pluggy.ValidateAsync(externalReference, CancellationToken.None);
        if (verified.Outcome != PluggyConnectionValidationOutcome.Succeeded)
        {
            return Failure(verified.Outcome);
        }

        var result = await connections.CreateAsync(
            new ConnectionCreation(
                profile.Id,
                TransactionSourceType.Pluggy,
                externalReference,
                protector.Protect(verified.AccessToken!),
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome == ConnectionMutationOutcome.ProfileNotFound)
        {
            return DataOutput<CreateConnectionCommandOutput?>.New
                .WithError(ConnectionMessages.ProfileNotFound);
        }

        return Result(result.Connection!, verified.Institution!, result.Outcome);
    }

    private static DataOutput<CreateConnectionCommandOutput?> Failure(
        PluggyConnectionValidationOutcome outcome)
    {
        var message = outcome switch
        {
            PluggyConnectionValidationOutcome.InvalidReference => ConnectionMessages.InvalidReference,
            PluggyConnectionValidationOutcome.Unavailable => ConnectionMessages.SourceUnavailable,
            PluggyConnectionValidationOutcome.NotConfigured => ConnectionMessages.SourceNotAvailable,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        return DataOutput<CreateConnectionCommandOutput?>.New.WithError(message);
    }

    private static DataOutput<CreateConnectionCommandOutput?> Result(
        ConnectionSnapshot connection,
        string institution,
        ConnectionMutationOutcome outcome)
    {
        var output = DataOutput<CreateConnectionCommandOutput?>.New
            .WithData(new CreateConnectionCommandOutput
            {
                Id = connection.Id,
                DataSourceType = connection.DataSourceType,
                ExternalReference = connection.ExternalReference,
                Institution = institution,
                Status = connection.Status,
                CreatedAt = connection.CreatedAt,
                UpdatedAt = connection.UpdatedAt
            });

        return outcome == ConnectionMutationOutcome.Succeeded
            ? output.WithMessage(ConnectionMessages.CreatedSuccessfully)
            : output.WithError(ConnectionMessages.Duplicate);
    }
}
