namespace ArturRios.Fortuna.Shared.Messages;

public static class BackgroundJobMessages
{
    public const string PayloadInvalid = "The background job payload is invalid.";
    public const string Failed = "The job failed.";

    public static string HandlerNotRegistered(string jobType) =>
        $"No handler is registered for job type '{jobType}'.";
}
