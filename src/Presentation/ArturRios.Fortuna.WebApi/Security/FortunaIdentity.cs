using ArturRios.Util.WebApi.Security.Interfaces;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// The caller reconstructed locally from a Heimdall token. The identifier is Heimdall's
/// public subject; Fortuna never stores or receives that identity's credentials.
/// </summary>
public sealed record FortunaIdentity(
    Guid Id,
    int RoleId,
    Guid? ScopeId,
    IReadOnlyCollection<string> Permissions) : IAuthenticatedUser
{
    public Guid SubjectId => Id;
    public string? DisplayName { get; init; }
    public bool IsLocal { get; init; }
}
