using System.Globalization;
using ArturRios.Fortuna.Domain.Exports;

namespace ArturRios.Fortuna.Command.Input.Validation;

/// <summary>
///     Parses the free-text parts of an export request. The validator and the handler share it,
///     so what one accepts the other can always read back, without exceptions.
/// </summary>
public static class DataExportInput
{
    // Built once: probing CultureInfo per request and catching CultureNotFoundException is
    // expensive, and ICU would otherwise accept any well-formed tag as a made-up culture.
    private static readonly Dictionary<string, string> SpecificCultures = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Where(culture => !string.IsNullOrEmpty(culture.Name))
        .GroupBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

    public static bool TryParseFormat(string? value, out DataExportFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "csv":
                format = DataExportFormat.Csv;

                return true;
            case "xlsx" or "excel":
                format = DataExportFormat.Excel;

                return true;
            case "pdf":
                format = DataExportFormat.Pdf;

                return true;
            default:
                format = default;

                return false;
        }
    }

    /// <summary>Resolves a specific culture name (for example <c>pt-br</c>) to its canonical form.</summary>
    public static bool TryResolveLocale(string? value, out string locale)
    {
        if (value is not null && SpecificCultures.TryGetValue(value.Trim(), out var name))
        {
            locale = name;

            return true;
        }

        locale = string.Empty;

        return false;
    }
}
