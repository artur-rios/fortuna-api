namespace ArturRios.Fortuna.Shared.Users;

public interface IUserProfileProvisioner
{
    Task<UserProfileSnapshot> GetOrCreateAsync(
        Guid externalSubject,
        string displayName,
        CancellationToken cancellationToken);
}
