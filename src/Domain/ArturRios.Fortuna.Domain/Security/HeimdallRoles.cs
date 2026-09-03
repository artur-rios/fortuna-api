namespace ArturRios.Fortuna.Domain.Security;

/// <summary>
/// The role values emitted by Heimdall. Keeping the numeric contract here lets Fortuna
/// authorize a token without calling the identity service.
/// </summary>
public enum HeimdallRoles
{
    SystemAdmin = 1,
    ScopeAdmin = 2,
    User = 3
}
