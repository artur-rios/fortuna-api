namespace ArturRios.Fortuna.Shared.Users;

public interface IUserProfileReader
{
    Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
        Guid externalSubject,
        CancellationToken cancellationToken);

    Task<UserProfileSnapshot?> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken);
}
