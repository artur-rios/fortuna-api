namespace ArturRios.Fortuna.Shared.Users;

/// <summary>
///     Resolves the profile of the user acting on the current request: a local actor is looked
///     up by the profile's public id, a Heimdall actor by its external subject.
/// </summary>
public interface ICurrentProfileResolver
{
    /// <summary>Returns the acting user's profile, or <c>null</c> when there is none.</summary>
    Task<UserProfileSnapshot?> ResolveAsync();
}
