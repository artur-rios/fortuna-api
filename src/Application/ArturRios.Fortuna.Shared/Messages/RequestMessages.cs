namespace ArturRios.Fortuna.Shared.Messages;

/// <summary>Messages for failures that happen before or around a handler, not inside it.</summary>
public static class RequestMessages
{
    public const string Invalid = "The request is invalid.";
    public const string UnexpectedError = "An unexpected error occurred. Try again later.";
}
