using ArturRios.Fortuna.Shared.Security;

namespace ArturRios.Fortuna.Shared.Users;

public sealed class CurrentProfileResolver(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles) : ICurrentProfileResolver
{
    public Task<UserProfileSnapshot?> ResolveAsync()
    {
        var actor = actorAccessor.Actor;
        if (actor is null)
        {
            return Task.FromResult<UserProfileSnapshot?>(null);
        }

        return actor.IsLocal
            ? profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
    }
}
