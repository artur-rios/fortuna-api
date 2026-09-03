namespace ArturRios.Fortuna.Shared.Users;

/// <summary>The credential-free profile shape shared across application boundaries.</summary>
public sealed record UserProfileSnapshot(
    Guid Id,
    Guid ExternalSubject,
    string DisplayName,
    string DisplayCurrency,
    bool DisplayCurrencyRequiresConfirmation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
