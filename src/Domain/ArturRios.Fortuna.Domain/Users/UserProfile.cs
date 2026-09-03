using ArturRios.Fortuna.Domain.Currencies;

namespace ArturRios.Fortuna.Domain.Users;

/// <summary>A local Fortuna profile keyed by an external Heimdall subject.</summary>
public sealed class UserProfile
{
    private UserProfile()
    {
    }

    public UserProfile(
        Guid externalSubject,
        string displayName,
        Currency displayCurrency,
        DateTimeOffset createdAt)
    {
        if (externalSubject == Guid.Empty)
        {
            throw new ArgumentException("An external subject is required.", nameof(externalSubject));
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200)
        {
            throw new ArgumentException("A display name between 1 and 200 characters is required.", nameof(displayName));
        }

        PublicId = Guid.NewGuid();
        ExternalSubject = externalSubject.ToString("D");
        DisplayName = displayName;
        DisplayCurrency = displayCurrency ?? throw new ArgumentNullException(nameof(displayCurrency));
        DisplayCurrencyId = displayCurrency.Id;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public string? ExternalSubject { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public long DisplayCurrencyId { get; private set; }
    public Currency DisplayCurrency { get; private set; } = null!;
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
