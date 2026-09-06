using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateConnectionCommandHandler(
    IValidator<CreateConnectionCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IConnectionStore connections,
    IPluggyConnectionGateway pluggy,
    IConnectionAccessTokenProtector protector,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateConnectionCommand, CreateConnectionCommandOutput>
{
    public async Task<DataOutput<CreateConnectionCommandOutput?>> HandleAsync(
        CreateConnectionCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<CreateConnectionCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return DataOutput<CreateConnectionCommandOutput?>.New.WithError(
                ConnectionMessages.ProfileNotFound);
        }

        var externalReference = Guid.Parse(command.ExternalReference.Trim()).ToString();
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
        return Result(result.Connection, verified.Institution!, result.Outcome);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

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
