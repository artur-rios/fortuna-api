namespace ArturRios.Fortuna.Query.Input.Validation;

internal static class InvestmentQueryValidation
{
    public static bool IsOptionalCurrencyCode(string? code) =>
        code is null || code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter);
}
