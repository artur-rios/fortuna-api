namespace ArturRios.Fortuna.Shared.Messages;

public static class CreditCardStatementMessages
{
    public const string ClosedSuccessfully = "Credit card statement closed successfully.";
    public const string NotDue = "Credit card statement remains open until its closing date passes.";
    public const string NotFound = "Credit card statement not found.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string SettledStatementFrozen =
        "A settled credit card statement is frozen and cannot be recomputed.";
}
