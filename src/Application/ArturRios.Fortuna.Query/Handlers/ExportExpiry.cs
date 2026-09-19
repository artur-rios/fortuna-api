using ArturRios.Fortuna.Shared.Exports;

namespace ArturRios.Fortuna.Query.Handlers;

/// <summary>
/// One expiry rule for data exports and personal data archives: once the retention
/// window has passed the export is reported as expired, whatever its status, before
/// its state or file is looked at.
/// </summary>
internal static class ExportExpiry
{
    public static bool HasExpired(DataExportReadSnapshot export, TimeProvider timeProvider) =>
        timeProvider.GetUtcNow() >= export.ExpiresAt;
}
