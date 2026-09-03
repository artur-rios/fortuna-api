namespace ArturRios.Fortuna.Shared.Users;

public interface IUserProfileReader
{
    Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
        Guid externalSubject,
        CancellationToken cancellationToken);
}
