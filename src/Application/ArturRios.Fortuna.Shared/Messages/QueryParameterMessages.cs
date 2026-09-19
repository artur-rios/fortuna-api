namespace ArturRios.Fortuna.Shared.Messages;

public static class QueryParameterMessages
{
    public static string Unsupported(string parameter) =>
        $"Query parameter '{parameter}' is not supported by this endpoint.";
}
