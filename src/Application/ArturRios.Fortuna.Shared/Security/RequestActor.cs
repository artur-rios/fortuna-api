namespace ArturRios.Fortuna.Shared.Security;

/// <summary>The authenticated Heimdall identity acting on the current request.</summary>
public sealed record RequestActor(
    Guid SubjectId,
    int RoleId,
    Guid? ScopeId,
    IReadOnlyCollection<string> Permissions)
{
    public string? DisplayName { get; init; }
    public bool IsLocal { get; init; }
}
