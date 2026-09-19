using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

/// <summary>
/// Rules shared by the query validators.
/// </summary>
public static class QueryValidationRules
{
    /// <summary>
    /// Accepts a missing or blank code (the query then falls back to a default) or three
    /// ASCII letters once surrounding whitespace is trimmed.
    /// </summary>
    public static IRuleBuilderOptions<T, TCode> OptionalCurrencyCode<T, TCode>(
        this IRuleBuilder<T, TCode> rule) =>
        rule.Must(code => code is not string value ||
            string.IsNullOrWhiteSpace(value) ||
            IsCurrencyCode(value));

    /// <summary>
    /// Requires three ASCII letters once surrounding whitespace is trimmed.
    /// </summary>
    public static IRuleBuilderOptions<T, TCode> TrimmedCurrencyCode<T, TCode>(
        this IRuleBuilder<T, TCode> rule) =>
        rule.Must(code => code is string value && IsCurrencyCode(value));

    public static readonly DateOnly MinimumAsOfDate = new(1900, 1, 1);
    public static readonly DateOnly MaximumAsOfDate = new(2100, 12, 31);

    /// <summary>
    /// Accepts a missing date (the query then uses today) or one between
    /// <see cref="MinimumAsOfDate"/> and <see cref="MaximumAsOfDate"/>.
    /// </summary>
    public static IRuleBuilderOptions<T, DateOnly?> OptionalAsOfDate<T>(
        this IRuleBuilder<T, DateOnly?> rule) =>
        rule.Must(date => date is null ||
            (date.Value >= MinimumAsOfDate && date.Value <= MaximumAsOfDate));

    public static bool IsCurrencyCode(string code)
    {
        var trimmed = code.Trim();

        return trimmed.Length == 3 && trimmed.All(char.IsAsciiLetter);
    }
}
