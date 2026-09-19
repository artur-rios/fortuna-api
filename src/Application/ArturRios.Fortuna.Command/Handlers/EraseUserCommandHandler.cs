using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class EraseUserCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICurrentProfileResolver profileResolver,
    IUserErasureStore erasure,
    IProvisionedProfileCache provisionedProfiles,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<EraseUserCommand, EraseUserCommandOutput>
{
    public async Task<DataOutput<EraseUserCommandOutput?>> HandleAsync(EraseUserCommand command)
    {
        var actor = actorAccessor.Actor;
        UserProfileSnapshot? target = null;
        if (command.IsSelfService && actor?.RoleId == (int)HeimdallRoles.User)
        {
            target = await profileResolver.ResolveAsync();
        }
        else if (!command.IsSelfService &&
                 actor?.RoleId == (int)HeimdallRoles.SystemAdmin &&
                 command.UserId.HasValue)
        {
            target = await profiles.FindByPublicIdAsync(command.UserId.Value, CancellationToken.None);
        }

        if (target is null)
        {
            return DataOutput<EraseUserCommandOutput?>.New.WithError(
                UserErasureMessages.UserNotFound);
        }

        var result = await erasure.EraseAsync(
            target.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        if (result is null)
        {
            return DataOutput<EraseUserCommandOutput?>.New.WithError(
                UserErasureMessages.UserNotFound);
        }

        if (target.ExternalSubject is { } externalSubject)
        {
            provisionedProfiles.Forget(externalSubject);
        }

        return DataOutput<EraseUserCommandOutput?>.New
            .WithData(new EraseUserCommandOutput
            {
                Erased = result.Erased,
                RevokedConnections = result.RevokedConnections
            })
            .WithMessage(UserErasureMessages.ErasedSuccessfully);
    }
}
