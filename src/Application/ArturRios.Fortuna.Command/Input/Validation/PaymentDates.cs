namespace ArturRios.Fortuna.Command.Input.Validation;

/// <summary>
///     The date rule shared by every money movement between an account and a statement or
///     another account: at most one day ahead, which absorbs time-zone skew and nothing more.
/// </summary>
public static class PaymentDates
{
    public static DateOnly Latest(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
}
