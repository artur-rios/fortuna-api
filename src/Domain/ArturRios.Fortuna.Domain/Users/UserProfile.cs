using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Domain.Users;

/// <summary>A local Fortuna profile keyed by an external Heimdall subject.</summary>
public sealed class UserProfile : RecordLifecycleEntity
{
    private UserProfile()
    {
    }

    public UserProfile(
        Guid externalSubject,
        string displayName,
        Currency displayCurrency,
        DateTimeOffset createdAt)
        : this(displayName, displayCurrency, createdAt)
    {
        if (externalSubject == Guid.Empty)
        {
            throw new ArgumentException("An external subject is required.", nameof(externalSubject));
        }

        ExternalSubject = externalSubject.ToString("D");
    }

    public UserProfile(
        string displayName,
        Currency displayCurrency,
        DateTimeOffset createdAt) : base(createdAt)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200)
        {
            throw new ArgumentException("A display name between 1 and 200 characters is required.", nameof(displayName));
        }

        DisplayName = displayName;
        DisplayCurrency = displayCurrency ?? throw new ArgumentNullException(nameof(displayCurrency));
        DisplayCurrencyId = displayCurrency.Id;
    }

    public long Id { get; private set; }
    public string? ExternalSubject { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public long DisplayCurrencyId { get; private set; }
    public Currency DisplayCurrency { get; private set; } = null!;
}
