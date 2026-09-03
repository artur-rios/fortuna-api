using ArturRios.Fortuna.Domain.Security;

namespace ArturRios.Fortuna.Shared.Security;

public enum FinancialRecordAccessResult
{
    Allowed,
    NotFound,
    Forbidden
}

/// <summary>
/// Centralizes Fortuna's two non-negotiable financial-data rules: ownership is exact,
/// and instance administration grants no access to user financial records.
/// </summary>
public static class FinancialRecordAccess
{
    public static FinancialRecordAccessResult Authorize(
        Guid actingSubjectId,
        int actingRoleId,
        Guid ownerSubjectId)
    {
        if (actingRoleId == (int)HeimdallRoles.SystemAdmin)
        {
            return FinancialRecordAccessResult.Forbidden;
        }

        return actingSubjectId == ownerSubjectId
            ? FinancialRecordAccessResult.Allowed
            : FinancialRecordAccessResult.NotFound;
    }
}
