using ArturRios.Fortuna.Shared.Users;

namespace ArturRios.Fortuna.Query.Conversion;

/// <summary>
/// Resolves the display currency of a query: the requested code when one is given,
/// otherwise the acting user's profile currency. A blank code counts as not given.
/// </summary>
internal static class DisplayCurrency
{
    public static string ResolveCode(string? requested, UserProfileSnapshot profile) =>
        string.IsNullOrWhiteSpace(requested)
            ? profile.DisplayCurrency.Trim().ToUpperInvariant()
            : requested.Trim().ToUpperInvariant();
}
