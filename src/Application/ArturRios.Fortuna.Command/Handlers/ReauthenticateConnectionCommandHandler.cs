using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ReauthenticateConnectionCommandHandler(
    ICurrentProfileResolver profileResolver,
    IConnectionReader connectionReader,
    IConnectionReauthenticationStore connections,
    IPluggyConnectionGateway pluggy,
    IConnectionAccessTokenProtector protector,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<ReauthenticateConnectionCommand,
        ReauthenticateConnectionCommandOutput>
{
    public async Task<DataOutput<ReauthenticateConnectionCommandOutput?>> HandleAsync(
        ReauthenticateConnectionCommand command)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return Output().WithError(ConnectionMessages.ProfileNotFound);
        }

        var existing = await connectionReader.FindByIdAsync(
            profile.Id, command.Id, CancellationToken.None);
        if (existing is null)
        {
            return Output().WithError(ConnectionMessages.NotFound);
        }

        if (existing.Status == ConnectionStatus.Revoked)
        {
            return Output().WithError(ConnectionMessages.Revoked);
        }

        if (existing.Status != ConnectionStatus.RequiresReauthentication)
        {
            return Output().WithError(ConnectionMessages.ReauthenticationNotRequired);
        }

        if (!Guid.TryParse(command.ExternalReference?.Trim(), out var itemId))
        {
            return Output().WithError(ConnectionMessages.ExternalReferenceInvalid);
        }

        var externalReference = itemId.ToString();
        var verified = await pluggy.ValidateAsync(externalReference, CancellationToken.None);
        if (verified.Outcome != PluggyConnectionValidationOutcome.Succeeded)
        {
            return PluggyFailure(verified.Outcome);
        }

        var result = await connections.ReauthenticateAsync(
            new ConnectionReauthentication(
                profile.Id,
                command.Id,
                externalReference,
                protector.Protect(verified.AccessToken!),
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        return Resolve(result, verified.Institution!);
    }

    private static DataOutput<ReauthenticateConnectionCommandOutput?> PluggyFailure(
        PluggyConnectionValidationOutcome outcome)
    {
        var message = outcome switch
        {
            PluggyConnectionValidationOutcome.InvalidReference => ConnectionMessages.InvalidReference,
            PluggyConnectionValidationOutcome.Unavailable => ConnectionMessages.SourceUnavailable,
            PluggyConnectionValidationOutcome.NotConfigured => ConnectionMessages.SourceNotAvailable,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        return Output().WithError(message);
    }

    private static DataOutput<ReauthenticateConnectionCommandOutput?> Resolve(
        ConnectionReauthenticationResult result,
        string institution)
    {
        var output = Output();
        if (result.Connection is not null)
        {
            output = output.WithData(new ReauthenticateConnectionCommandOutput
            {
                Id = result.Connection.Id,
                DataSourceType = result.Connection.DataSourceType,
                ExternalReference = result.Connection.ExternalReference,
                Institution = institution,
                Status = result.Connection.Status,
                CreatedAt = result.Connection.CreatedAt,
                UpdatedAt = result.Connection.UpdatedAt
            });
        }

        return result.Outcome switch
        {
            ConnectionReauthenticationOutcome.Succeeded => output.WithMessage(
                ConnectionMessages.ReauthenticatedSuccessfully),
            ConnectionReauthenticationOutcome.NotFound => output.WithError(ConnectionMessages.NotFound),
            ConnectionReauthenticationOutcome.NotRequired => output.WithError(
                ConnectionMessages.ReauthenticationNotRequired),
            ConnectionReauthenticationOutcome.Revoked => output.WithError(ConnectionMessages.Revoked),
            ConnectionReauthenticationOutcome.DuplicateReference => output.WithError(
                ConnectionMessages.DuplicateReference),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static DataOutput<ReauthenticateConnectionCommandOutput?> Output() =>
        DataOutput<ReauthenticateConnectionCommandOutput?>.New;
}
