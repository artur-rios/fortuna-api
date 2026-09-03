namespace ArturRios.Fortuna.Shared.Users;

public sealed record LocalAccountOptions(
    bool Enabled,
    int RecoveryCodeCount,
    string? DefaultDisplayCurrency,
    string Locale);
