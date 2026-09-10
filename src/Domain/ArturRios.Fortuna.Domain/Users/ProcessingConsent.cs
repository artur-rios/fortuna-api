namespace ArturRios.Fortuna.Domain.Users;

public enum ProcessingConsentPurpose : short
{
    ExternalDataProcessing = 1
}

public sealed class ProcessingConsent
{
    private ProcessingConsent()
    {
    }

    public ProcessingConsent(
        UserProfile user,
        ProcessingConsentPurpose purpose,
        string version,
        DateTimeOffset grantedAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        PublicId = Guid.NewGuid();
        UserId = user.Id;
        Purpose = purpose;
        Version = RequiredVersion(version);
        GrantedAt = grantedAt;
        UpdatedAt = grantedAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public ProcessingConsentPurpose Purpose { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Grant(string version, DateTimeOffset grantedAt)
    {
        Version = RequiredVersion(version);
        GrantedAt = grantedAt;
        UpdatedAt = grantedAt;
    }

    private static string RequiredVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A consent version is required.", nameof(value));
        }
        var normalized = value.Trim();
        return normalized.Length <= 50
            ? normalized
            : throw new ArgumentException(
                "A consent version cannot exceed 50 characters.", nameof(value));
    }
}
