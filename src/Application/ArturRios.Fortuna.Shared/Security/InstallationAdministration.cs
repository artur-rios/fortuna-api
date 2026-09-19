using ArturRios.Fortuna.Domain.Security;

namespace ArturRios.Fortuna.Shared.Security;

/// <summary>
/// Decides who may change installation-wide data such as the global exchange-rate table: a
/// Heimdall system administrator, or the owner of an offline installation signed in with its
/// single local account (who has no administrator to ask).
/// </summary>
public static class InstallationAdministration
{
    public static bool IsAdministrator(int roleId, bool isLocal) =>
        roleId == (int)HeimdallRoles.SystemAdmin || isLocal;

    public static bool IsAdministrator(RequestActor? actor) =>
        actor is not null && IsAdministrator(actor.RoleId, actor.IsLocal);
}
