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
        var response = Response(operation, status);

        return response.TryGetProperty("content", out var content)
            ? content.EnumerateObject()
                .Select(media => KeyValuePair.Create(media.Name, media.Value.GetProperty("schema").Clone()))
                .ToArray()
            : [];
    }

    /// <summary>The response an operation (<c>"GET /api/exports/{id}"</c>) documents for a status.</summary>
    public static JsonElement Response(string operation, int status)
    {
        using var document = Load();
        var separator = operation.IndexOf(' ', StringComparison.Ordinal);

        return document.RootElement.GetProperty("paths")
            .GetProperty(operation[(separator + 1)..])
            .GetProperty(operation[..separator].ToLowerInvariant())
            .GetProperty("responses")
            .GetProperty(status.ToString(CultureInfo.InvariantCulture))
            .Clone();
    }

    /// <summary>The headers a response documents, by name.</summary>
    public static IReadOnlyList<string> Headers(string operation, int status) =>
        Response(operation, status).TryGetProperty("headers", out var headers)
            ? headers.EnumerateObject().Select(header => header.Name).ToArray()
            : [];

    /// <summary>
    ///     Whether <paramref name="body" /> validates against <paramref name="schema" /> as far as an envelope
    ///     needs: a reference is followed, every member must be a property the schema declares unless it admits
    ///     additional ones, and each member's value must be of a type its property admits (<c>null</c> only where
    ///     the property is nullable). <paramref name="failure" /> says why not.
    /// </summary>
    public static bool Accepts(JsonElement schema, JsonElement body, out string failure)
    {
        using var document = Load();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var resolved = Resolve(schema, schemas);
        if (body.ValueKind != JsonValueKind.Object)
        {
            failure = $"the body is {body.ValueKind}, not an object";

            return false;
        }

        var properties = resolved.TryGetProperty("properties", out var declared) ? declared : default;
        var closed = resolved.TryGetProperty("additionalProperties", out var additional) &&
                     additional.ValueKind == JsonValueKind.False;
        foreach (var member in body.EnumerateObject())
        {
            if (properties.ValueKind != JsonValueKind.Object ||
                !properties.TryGetProperty(member.Name, out var property))
            {
                if (closed)
                {
                    failure = $"`{member.Name}` is not a property and additionalProperties is false";

                    return false;
                }

                continue;
            }

            if (!Admits(Resolve(property, schemas), member.Value, schemas))
            {
                failure = $"`{member.Name}` is {member.Value.ValueKind}, which {property} does not admit";

                return false;
            }
        }

        failure = string.Empty;

        return true;
    }

    private static bool Admits(JsonElement schema, JsonElement value, JsonElement schemas)
    {
        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            return anyOf.EnumerateArray().Any(option => Admits(Resolve(option, schemas), value, schemas));
        }

        if (!schema.TryGetProperty("type", out var type))
        {
            return value.ValueKind != JsonValueKind.Null || !schema.TryGetProperty("properties", out _);
        }

        var types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [type.GetString()!];
        var kind = value.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
            _ => "undefined"
        };

        return types.Contains(kind) || (kind == "integer" && types.Contains("number"));
    }

    private static JsonElement Resolve(JsonElement schema, JsonElement schemas) =>
        schema.TryGetProperty("$ref", out var reference)
            ? schemas.GetProperty(reference.GetString()!["#/components/schemas/".Length..])
            : schema;

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
