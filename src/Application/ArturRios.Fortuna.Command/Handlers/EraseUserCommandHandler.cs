using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class EraseUserCommandHandler(
    IValidator<EraseUserCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IUserErasureStore erasure,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<EraseUserCommand, EraseUserCommandOutput>
{
    public async Task<DataOutput<EraseUserCommandOutput?>> HandleAsync(EraseUserCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<EraseUserCommandOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        UserProfileSnapshot? target = null;
        if (command.IsSelfService && actor?.RoleId == (int)HeimdallRoles.User)
        {
            target = actor.IsLocal
                ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
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

        return DataOutput<EraseUserCommandOutput?>.New
            .WithData(new EraseUserCommandOutput
            {
                Erased = result.Erased,
                RevokedConnections = result.RevokedConnections
            })
            .WithMessage(UserErasureMessages.ErasedSuccessfully);
    }
}
