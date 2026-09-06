namespace ArturRios.Fortuna.Shared.Messages;

public static class PluggySynchronizationMessages
{
    public const string Accepted = "Connection synchronization queued successfully.";
    public const string ConnectionNotFound = "Connection not found.";
    public const string ConnectionInactive = "The connection is not active.";
    public const string AlreadyRunning = "A synchronization is already running for this connection.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string PeriodInvalid = "PeriodStart cannot follow PeriodEnd.";
    public const string ReauthenticationRequired = "The connection requires reauthentication.";
    public const string SourceUnavailable = "Pluggy is temporarily unavailable.";
    public const string AccountNotMapped = "The source account could not be mapped.";
    public const string TransactionInvalid = "The source transaction is invalid.";
}
