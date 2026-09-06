using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RevokeConnectionCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IConnectionRevocationStore connections,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RevokeConnectionCommand, RevokeConnectionCommandOutput>
{
    public async Task<DataOutput<RevokeConnectionCommandOutput?>> HandleAsync(
        RevokeConnectionCommand command)
    {
        var output = DataOutput<RevokeConnectionCommandOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(ConnectionMessages.ProfileNotFound);
        }

        var result = await connections.RevokeAsync(
            new ConnectionRevocation(profile.Id, command.Id, timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Connection is null)
        {
            return output.WithError(ConnectionMessages.NotFound);
        }

        output = output.WithData(new RevokeConnectionCommandOutput
        {
            Id = result.Connection.Id,
            DataSourceType = result.Connection.DataSourceType,
            ExternalReference = result.Connection.ExternalReference,
            Status = result.Connection.Status,
            ImportedDataRetained = true,
            StoppedSynchronizations = result.StoppedSynchronizations,
            CreatedAt = result.Connection.CreatedAt,
            UpdatedAt = result.Connection.UpdatedAt
        });
        return result.Outcome switch
        {
            ConnectionRevocationOutcome.Succeeded => output.WithMessage(
                ConnectionMessages.RevokedSuccessfully),
            ConnectionRevocationOutcome.AlreadyRevoked => output.WithMessage(
                ConnectionMessages.AlreadyRevoked),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
