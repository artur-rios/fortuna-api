using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Shared.Users;

public sealed record ProcessingConsentOptions(string ExternalDataProcessingVersion)
{
    public const string ExternalDataProcessingName = "external-data-processing";

    public string CurrentVersion(ProcessingConsentPurpose purpose) => purpose switch
    {
        ProcessingConsentPurpose.ExternalDataProcessing => ExternalDataProcessingVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };

    public static bool TryParsePurpose(
        string? value,
        out ProcessingConsentPurpose purpose)
    {
        if (string.Equals(
                value?.Trim(),
                ExternalDataProcessingName,
                StringComparison.OrdinalIgnoreCase))
        {
            purpose = ProcessingConsentPurpose.ExternalDataProcessing;
            return true;
        }
        purpose = default;
        return false;
    }

    public static string Name(ProcessingConsentPurpose purpose) => purpose switch
    {
        ProcessingConsentPurpose.ExternalDataProcessing => ExternalDataProcessingName,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}

public sealed record ProcessingConsentSnapshot(
    Guid Id,
    ProcessingConsentPurpose Purpose,
    string Version,
    DateTimeOffset GrantedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProcessingConsentWithdrawal(
    bool Found,
    int RevokedConnections,
    int StoppedSynchronizations);

public interface IProcessingConsentReader
{
    Task<IReadOnlyCollection<ProcessingConsentSnapshot>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> IsCurrentAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        string version,
        CancellationToken cancellationToken);
}

public interface IProcessingConsentStore : IProcessingConsentReader
{
    Task<ProcessingConsentSnapshot> GrantAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        string version,
        DateTimeOffset grantedAt,
        CancellationToken cancellationToken);

    Task<ProcessingConsentWithdrawal> WithdrawAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken);
}
