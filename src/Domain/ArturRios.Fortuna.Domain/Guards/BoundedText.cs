namespace ArturRios.Fortuna.Domain.Guards;

/// <summary>
/// Normalizes free text held by the domain: surrounding whitespace is trimmed before the length
/// bound is checked, so the stored value and the checked value are always the same string.
/// </summary>
internal static class BoundedText
{
    /// <summary>Returns the trimmed value, rejecting a blank value or one longer than the bound.</summary>
    public static string Required(
        string? value,
        int maximumLength,
        string parameterName,
        string? message = null)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                message ?? $"A value between 1 and {maximumLength} characters is required.",
                parameterName);
        }

        return normalized;
    }

    /// <summary>Returns the trimmed value or null when blank, rejecting one longer than the bound.</summary>
    public static string? Optional(
        string? value,
        int maximumLength,
        string parameterName,
        string? message = null)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                message ?? $"A value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
