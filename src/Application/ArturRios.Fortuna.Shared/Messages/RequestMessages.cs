namespace ArturRios.Fortuna.Shared.Messages;

/// <summary>Messages for failures that happen before or around a handler, not inside it.</summary>
public static class RequestMessages
{
    public const string Invalid = "The request is invalid.";
    public const string UnexpectedError = "An unexpected error occurred. Try again later.";
    public const string BodyTooLarge = "The request body is larger than the server accepts.";
    public const string BodyTooSlow = "The request body arrived too slowly.";
    public const string Unreadable = "The request could not be read: it is malformed or incomplete.";
}
