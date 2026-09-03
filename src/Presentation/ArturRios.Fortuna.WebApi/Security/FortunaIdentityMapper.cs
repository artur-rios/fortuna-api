using System.Text.Json;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Util.WebApi.Security.Constants;
using ArturRios.Util.WebApi.Security.Interfaces;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Reads the claim vocabulary Heimdall writes. Invalid identity claims produce no caller,
/// allowing the authentication middleware to return 401 rather than leaking parser details.
/// </summary>
public sealed class FortunaIdentityMapper : IAuthenticatedUserMapper
{
    public const string SubjectClaim = TokenClaimKeys.Id;
    public const string RoleClaim = TokenClaimKeys.RoleId;
    public const string DisplayNameClaim = "name";
    public const string ScopeClaim = "scopeId";
    public const string PermissionsClaim = "scopePermissions";

    public Dictionary<string, string> ToClaims(IAuthenticatedUser user)
    {
        var claims = new Dictionary<string, string>
        {
            [SubjectClaim] = user.Id.ToString(),
            [RoleClaim] = user.RoleId.ToString()
        };

        if (user is not FortunaIdentity identity)
        {
            return claims;
        }

        if (identity.ScopeId is not null)
        {
            claims[ScopeClaim] = identity.ScopeId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            claims[DisplayNameClaim] = identity.DisplayName;
        }

        if (identity.Permissions.Count > 0)
        {
            claims[PermissionsClaim] = JsonSerializer.Serialize(identity.Permissions);
        }

        return claims;
    }

    public IAuthenticatedUser? FromClaims(IReadOnlyDictionary<string, string> claims)
    {
        if (!claims.TryGetValue(SubjectClaim, out var rawSubject) ||
            !Guid.TryParse(rawSubject, out var subjectId) ||
            !claims.TryGetValue(RoleClaim, out var rawRole) ||
            !int.TryParse(rawRole, out var roleId) ||
            !Enum.IsDefined(typeof(HeimdallRoles), roleId))
        {
            return null;
        }

        var scopeId = claims.TryGetValue(ScopeClaim, out var rawScope) &&
                      Guid.TryParse(rawScope, out var parsedScope)
            ? parsedScope
            : (Guid?)null;

        return new FortunaIdentity(subjectId, roleId, scopeId, ReadPermissions(claims))
        {
            DisplayName = claims.GetValueOrDefault(DisplayNameClaim)
        };
    }

    private static IReadOnlyCollection<string> ReadPermissions(
        IReadOnlyDictionary<string, string> claims)
    {
        if (!claims.TryGetValue(PermissionsClaim, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
