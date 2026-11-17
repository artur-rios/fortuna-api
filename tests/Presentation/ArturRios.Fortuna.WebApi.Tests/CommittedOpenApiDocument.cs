using System.Globalization;
using System.Text.Json;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Reads docs/openapi/fortuna.json, the document clients are generated from, which the check job keeps
///     what the build generates.
/// </summary>
internal static class CommittedOpenApiDocument
{
    public static JsonDocument Load() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "openapi", "fortuna.json")));

    /// <summary>
    ///     The media types an operation (<c>"GET /api/exports/{id}"</c>) lists for a status, in the document's
    ///     order, each with its schema; empty when the status lists no content.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, JsonElement>> Content(string operation, int status)
    {
        using var document = Load();
        var separator = operation.IndexOf(' ', StringComparison.Ordinal);
        var response = document.RootElement.GetProperty("paths")
            .GetProperty(operation[(separator + 1)..])
            .GetProperty(operation[..separator].ToLowerInvariant())
            .GetProperty("responses")
            .GetProperty(status.ToString(CultureInfo.InvariantCulture));

        return response.TryGetProperty("content", out var content)
            ? content.EnumerateObject()
                .Select(media => KeyValuePair.Create(media.Name, media.Value.GetProperty("schema").Clone()))
                .ToArray()
            : [];
    }

    /// <summary>Whether the schema is exactly <c>{ "type": "string", "format": "binary" }</c>: raw bytes.</summary>
    public static bool IsBinary(JsonElement schema) =>
        schema.EnumerateObject().Select(keyword => keyword.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(["format", "type"]) &&
        schema.GetProperty("type").GetString() == "string" &&
        schema.GetProperty("format").GetString() == "binary";

    /// <summary>
    ///     Whether the document lists <paramref name="mediaType" /> (a response's <c>Content-Type</c> without
    ///     its parameters) for the operation's status with a binary schema.
    /// </summary>
    public static bool DocumentsFile(string operation, int status, string mediaType) =>
        Content(operation, status).Any(media => media.Key == mediaType && IsBinary(media.Value));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "docs", "openapi", "fortuna.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
