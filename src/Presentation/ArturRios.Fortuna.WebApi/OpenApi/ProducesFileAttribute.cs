namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Documents that an action answers <paramref name="statusCode" /> with a file: the raw bytes of one of
///     <paramref name="contentTypes" />, not JSON. <see cref="FileResponseOperationFilter" /> publishes each as
///     its own media type with a binary schema, beside any JSON envelope the same status may carry. It only
///     describes the response: unlike <c>[Produces]</c>, it is no filter and changes nothing on the wire.
/// </summary>
/// <param name="statusCode">The status the file is sent with.</param>
/// <param name="contentTypes">
///     The media types the file can have, without parameters (<c>text/csv</c> for
///     <c>text/csv; charset=utf-8</c>). Binary types come before text ones: a client generator that reads
///     the first media type of a response decodes a <c>text/*</c> one as a string, which would corrupt a
///     workbook or a PDF.
/// </param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProducesFileAttribute(int statusCode, params string[] contentTypes) : Attribute
{
    public int StatusCode { get; } = statusCode;

    public IReadOnlyList<string> ContentTypes { get; } = contentTypes;

    /// <summary>The response's description, saying when the file is sent. The reason phrase when unset.</summary>
    public string? Description { get; set; }
}
