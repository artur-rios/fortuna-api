namespace ArturRios.Fortuna.Shared.Users;

public sealed record UserProfileProvisioningOptions(
    string? DefaultDisplayCurrency,
    string Locale);
