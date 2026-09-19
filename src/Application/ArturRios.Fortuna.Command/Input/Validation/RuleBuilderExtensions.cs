using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

/// <summary>Rules shared by every command validator, so one input shape is judged one way.</summary>
public static class RuleBuilderExtensions
{
    /// <summary>Monetary columns are numeric(19, 4): at most four decimals and fifteen integer digits.</summary>
    public const int MoneyScale = 4;

    public const decimal MoneyMagnitudeLimit = 1_000_000_000_000_000m;

    /// <summary>The amount fits the monetary storage precision (19, 4).</summary>
    public static IRuleBuilderOptions<T, decimal> Money<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(FitsMoneyStorage);

    /// <summary>A present amount fits the monetary storage precision (19, 4).</summary>
    public static IRuleBuilderOptions<T, decimal?> Money<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(amount => amount is null || FitsMoneyStorage(amount.Value));

    /// <summary>The code is three ASCII letters once surrounding whitespace is trimmed.</summary>
    public static IRuleBuilderOptions<T, string?> CurrencyCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(IsCurrencyCode);

    /// <summary>A blank code is accepted; any other must be three ASCII letters once trimmed.</summary>
    public static IRuleBuilderOptions<T, string?> OptionalCurrencyCode<T>(
        this IRuleBuilder<T, string?> rule) =>
        rule.Must(code => string.IsNullOrWhiteSpace(code) || IsCurrencyCode(code));

    /// <summary>
    ///     The text, once trimmed the way the domain stores it, is at most <paramref name="maximum" />
    ///     characters; a null value is left to the required rule.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> TrimmedMaximumLength<T>(
        this IRuleBuilder<T, string?> rule,
        int maximum) =>
        rule.Must(value => value is null || value.Trim().Length <= maximum);

    public static bool FitsMoneyStorage(decimal amount) =>
        amount.Scale <= MoneyScale && Math.Abs(amount) < MoneyMagnitudeLimit;

    public static bool IsCurrencyCode(string? code)
    {
        var trimmed = code?.Trim();

        return trimmed is { Length: 3 } && trimmed.All(char.IsAsciiLetter);
    }
}
