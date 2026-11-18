using System.Reflection;
using System.Text.Json;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Ties every error response docs/openapi/fortuna.json lists to what answers it, read here from the code
///     the way the server reads it: the controllers' status maps, the actions' parameters and attributes, and
///     the pipeline every request goes through. And checks that each one's schema accepts the bodies the
///     server really writes.
/// </summary>
public sealed class OpenApiErrorResponseTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [UnitFact]
    public void GivenEveryOperation_WhenDocumented_ThenItListsExactlyTheErrorStatusesItsSourcesAnswer()
    {
        var checkedOperations = 0;
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            using var contract = CommittedOpenApiDocument.Load();
            var documented = Operation(contract, key).GetProperty("responses").EnumerateObject()
                .Select(response => int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture))
                .Where(status => status >= 400 && status != 416)
                .Order()
                .ToArray();

            var expected = ExpectedErrorStatuses(key, action).Order().ToArray();

            Assert.True(expected.SequenceEqual(documented),
                $"{key} documents [{string.Join(", ", documented)}] but its sources answer " +
                $"[{string.Join(", ", expected)}].");
            checkedOperations++;
        }

        Assert.True(checkedOperations > 140, $"Only {checkedOperations} operations were checked.");
    }

    [UnitFact]
    public void GivenEveryJsonErrorResponse_WhenDocumented_ThenItsSchemaAcceptsTheBodiesTheServerWrites()
    {
        // The handler's envelope with a null data, model binding's, the upload limit's and the exception
        // handler's DataOutput<object?> (the same members) and [RoleRequirement]'s ProcessOutput.
        var envelope = Serialized(DataOutput<object?>.New.WithError("refused"));
        var process = Serialized(ProcessOutput.New.WithError("You do not have permission to access this resource"));
        var problem = Serialized(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            Title = "Forbidden",
            Status = 403,
            Extensions = { ["traceId"] = "00-50cd39c31ee042c317473b56c5519b67-bd57877f39c5a284-02" }
        });
        var checkedSchemas = 0;

        foreach (var (operation, status, mediaType, schema) in ErrorMediaTypes())
        {
            if (operation == "GET /healthcheck/detailed" && status == 503)
            {
                continue; // the action's own OperationalHealthOutput, which API Explorer documents
            }

            if (mediaType == "text/plain")
            {
                // Traefik's reason phrase.
                Assert.Equal("string", schema.GetProperty("type").GetString());

                continue;
            }

            JsonElement[] bodies = mediaType == "application/problem+json" ? [problem]
                : schema.GetProperty("$ref").GetString()!.EndsWith("/ProcessOutput", StringComparison.Ordinal)
                    ? [process]
                    : [envelope, process];
            foreach (var body in bodies)
            {
                Assert.True(CommittedOpenApiDocument.Accepts(schema, body, out var failure),
                    $"{operation} {status} {mediaType} rejects {body}: {failure}.");
            }

            checkedSchemas++;
        }

        Assert.True(checkedSchemas > 1000, $"Only {checkedSchemas} error schemas were checked.");
    }

    [UnitFact]
    public void GivenTheBearerChallenge_WhenDocumented_ThenEveryProtectedOperationsUnauthorizedNamesItsHeader()
    {
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            var anonymous = Attributes(action).OfType<IAllowAnonymous>().Any();
            var headers = CommittedOpenApiDocument.Headers(key, 401);
            var content = CommittedOpenApiDocument.Content(key, 401);

            // JWT authentication challenges with WWW-Authenticate and no body; the identity middlewares refuse
            // with no body either, so a 401 has a body only where the handler answers one.
            Assert.Equal(!anonymous, headers.Contains("WWW-Authenticate"));
            Assert.Equal(HandlerStatuses(action).Statuses.Contains(401), content.Count > 0);
        }
    }

    [UnitFact]
    public void GivenUnsupportedBodies_WhenDocumented_ThenABodyAnswersProblemDetailsAndAFormNothing()
    {
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            var body = action.GetParameters()
                .Any(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
            var form = Attributes(action).OfType<ConsumesAttribute>().Any();
            if (!body && !form)
            {
                continue;
            }

            var content = CommittedOpenApiDocument.Content(key, 415).Select(media => media.Key);
            string[] expected = body ? ["application/problem+json"] : [];

            Assert.Equal(expected, content);
        }
    }

    [UnitFact]
    public void GivenRateLimitedOperations_WhenDocumented_ThenOnlyARelayedRefusalCarriesRetryAfter()
    {
        var limited = 0;
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            var handler = HandlerStatuses(action);
            if (!Attributes(action).OfType<EnableRateLimitingAttribute>().Any() && !handler.Statuses.Contains(429))
            {
                continue;
            }

            limited++;

            // The rate limiter sets no Retry-After and writes no body; AuthController relays Heimdall's.
            Assert.Equal(handler.Relays, CommittedOpenApiDocument.Headers(key, 429).Contains("Retry-After"));
            Assert.Equal(handler.Statuses.Contains(429), CommittedOpenApiDocument.Content(key, 429).Count > 0);
        }

        Assert.True(limited > 10, $"Only {limited} rate-limited operations were found.");
    }

    [UnitFact]
    public void GivenTheExceptionHandler_WhenDocumented_ThenEveryOperations500IsJsonOnlyUnlessAHandlerAnswersIt()
    {
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            // WriteAsJsonAsync, whatever the request accepts; a handler's 500 (TransactionsController maps a
            // failed attachment write to it) is negotiated like any envelope.
            string[] expected = HandlerStatuses(action).Statuses.Contains(500)
                ? ["application/json", "text/json"]
                : ["application/json"];

            Assert.Equal(expected, CommittedOpenApiDocument.Content(key, 500).Select(media => media.Key));
        }
    }

    [UnitFact]
    public void GivenARoleOnlyRefusal_WhenDocumented_ThenItIsProcessOutput()
    {
        var content = CommittedOpenApiDocument.Content("GET /healthcheck/detailed", 403);

        Assert.All(content, media =>
            Assert.Equal("#/components/schemas/ProcessOutput", media.Value.GetProperty("$ref").GetString()));
        Assert.Equal(["application/json", "text/json"], content.Select(media => media.Key));
    }

    [UnitFact]
    public void GivenEveryResponse_WhenDocumented_ThenOnlyTraefiksListTextPlain()
    {
        using var contract = CommittedOpenApiDocument.Load();

        // StringOutputFormatter writes only strings, and no action answers one: an envelope asked for as
        // text/plain is written as application/json. Traefik's 502 and 504 are its reason phrase, as text/plain.
        var listed = contract.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .SelectMany(operation => operation.Value.GetProperty("responses").EnumerateObject()
                    .Where(response => response.Value.TryGetProperty("content", out var content) &&
                                       content.TryGetProperty("text/plain", out _))
                    .Select(response => response.Name)))
            .ToArray();

        Assert.NotEmpty(listed);
        Assert.All(listed, status => Assert.Contains(status, new[] { "502", "504" }));
    }

    [UnitFact]
    public void GivenEveryOperation_WhenDocumented_ThenKestrelsRequestRefusalsAreListedWithoutABody()
    {
        const string Kestrel = "Kestrel refused the request before the API saw it";
        foreach (var (key, _) in OpenApiDocumentAccuracyTests.Actions())
        {
            foreach (var status in new[] { 408, 414, 431, 505 })
            {
                Assert.Empty(CommittedOpenApiDocument.Content(key, status));
                Assert.Contains(Kestrel, Description(key, status), StringComparison.Ordinal);
            }

            // Its 400 has no body either: the description says so beside any source that writes one.
            Assert.Contains(Kestrel + ": a malformed request line or header", Description(key, 400),
                StringComparison.Ordinal);
        }
    }

    [UnitFact]
    public void GivenEveryOperation_WhenDocumented_ThenTraefiksBadGatewayAndGatewayTimeoutAreTextPlain()
    {
        foreach (var (key, _) in OpenApiDocumentAccuracyTests.Actions())
        {
            foreach (var status in new[] { 502, 504 })
            {
                var content = CommittedOpenApiDocument.Content(key, status);

                Assert.Equal(["text/plain"], content.Select(media => media.Key));
                Assert.Contains("Traefik", Description(key, status), StringComparison.Ordinal);
            }
        }
    }

    [UnitFact]
    public void GivenBodiesKestrelCanRefuse_WhenDocumented_ThenEachNamesTheStatusTheRefusalReallyGets()
    {
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            var body = action.GetParameters()
                .Any(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
            var form = Attributes(action).OfType<ConsumesAttribute>().Any();

            // A JSON body's refusal is a BadHttpRequestException that reaches the exception handler; a form's
            // fails the form value provider, which model binding answers.
            Assert.Equal(body, Description(key, 500).Contains("Kestrel refused the request body",
                StringComparison.Ordinal));
            Assert.Equal(form, Description(key, 400).Contains("The form could not be read",
                StringComparison.Ordinal));
        }
    }

    [UnitFact]
    public void GivenEveryOperation_WhenDocumented_ThenAFailureEscapingTheExceptionHandlerIsStatedOnIts500()
    {
        foreach (var (key, _) in OpenApiDocumentAccuracyTests.Actions())
        {
            Assert.Contains("answered by Kestrel with this status and no body", Description(key, 500),
                StringComparison.Ordinal);
        }
    }

    [UnitFact]
    public void GivenTheDocument_WhenRead_ThenItsDescriptionStatesWhatNoResponseCanSay()
    {
        using var contract = CommittedOpenApiDocument.Load();
        var description = contract.RootElement.GetProperty("info").GetProperty("description").GetString()!;

        Assert.Contains("Handler statuses are an upper bound", description, StringComparison.Ordinal);
        Assert.Contains("No response at all", description, StringComparison.Ordinal);
        Assert.Contains("stream reset", description, StringComparison.Ordinal);
        Assert.Contains("Cloudflare", description, StringComparison.Ordinal);
        Assert.Contains("developer exception page", description, StringComparison.Ordinal);
        Assert.Contains("405", description, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenAllowedQueryActions_WhenDocumented_ThenTheir400NamesTheUnknownQueryParameter()
    {
        var checkedActions = 0;
        foreach (var (key, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            var allowed = action.GetCustomAttribute<AllowedQueryAttribute>() is not null;

            Assert.Equal(allowed, Description(key, 400).Contains("A query parameter the operation does not read",
                StringComparison.Ordinal));
            checkedActions += allowed ? 1 : 0;
        }

        Assert.True(checkedActions > 5, $"Only {checkedActions} actions carry [AllowedQuery].");
    }

    private static string Description(string operation, int status) =>
        CommittedOpenApiDocument.Response(operation, status).GetProperty("description").GetString()!;

    /// <summary>What answers an error on the action, read from the code; mirrors no part of the filter.</summary>
    private static HashSet<int> ExpectedErrorStatuses(string key, MethodInfo action)
    {
        var attributes = Attributes(action).ToArray();
        // 401 from the identity middlewares, 500 from the exception handler, Kestrel's request refusals and
        // Traefik's answers: on every operation.
        var expected = new HashSet<int> { 400, 401, 408, 414, 431, 500, 502, 504, 505 };
        var handler = HandlerStatuses(action);
        expected.UnionWith(handler.Statuses);

        // Whatever MVC binds beyond the route: a body, a form or a query value.
        var routeValues = key.Split('/').Where(segment => segment.StartsWith('{'))
            .Select(segment => segment.Trim('{', '}'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (action.GetParameters().Any(parameter => parameter.ParameterType != typeof(CancellationToken) &&
                                                    !routeValues.Contains(parameter.Name!)) ||
            attributes.OfType<UploadLimitAttribute>().Any())
        {
            expected.Add(400);
        }

        if (attributes.OfType<RoleRequirementAttribute>().Any() ||
            attributes.OfType<InstallationAdministratorRequirementAttribute>().Any())
        {
            expected.Add(403);
        }

        if (action.GetParameters().Any(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null) ||
            attributes.OfType<ConsumesAttribute>().Any())
        {
            expected.Add(415);
        }

        if (attributes.OfType<EnableRateLimitingAttribute>().Any())
        {
            expected.Add(429);
        }

        expected.UnionWith(attributes.OfType<ProducesResponseTypeAttribute>()
            .Select(declared => declared.StatusCode)
            .Where(status => status >= 400));

        return expected;
    }

    /// <summary>
    ///     What the action's handlers can answer, as <see cref="OpenApiHandlerStatusTests.Expected" /> works it out
    ///     from the handler-message catalog: the statuses, and whether a relayed refusal answers 429.
    /// </summary>
    private static (HashSet<int> Statuses, bool Relays) HandlerStatuses(MethodInfo action)
    {
        if (!action.DeclaringType!.IsSubclassOf(typeof(FortunaController)))
        {
            return (new HashSet<int>(), false);
        }

        var expected = OpenApiHandlerStatusTests.Expected(action);

        return (expected.Statuses.Keys.ToHashSet(), expected.RelaysRetryAfter);
    }

    private static IEnumerable<object> Attributes(MethodInfo action) =>
        action.GetCustomAttributes(inherit: true).Concat(action.DeclaringType!.GetCustomAttributes(inherit: true));

    private static IEnumerable<(string Operation, int Status, string MediaType, JsonElement Schema)>
        ErrorMediaTypes()
    {
        using var contract = CommittedOpenApiDocument.Load();
        var found = new List<(string, int, string, JsonElement)>();
        foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
                {
                    var status = int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture);
                    if (status < 400 || !response.Value.TryGetProperty("content", out var content))
                    {
                        continue;
                    }

                    found.AddRange(content.EnumerateObject().Select(media => (
                        $"{operation.Name.ToUpperInvariant()} {path.Name}", status, media.Name,
                        media.Value.GetProperty("schema").Clone())));
                }
            }
        }

        return found;
    }

    private static JsonElement Operation(JsonDocument contract, string key)
    {
        var separator = key.IndexOf(' ', StringComparison.Ordinal);

        return contract.RootElement.GetProperty("paths").GetProperty(key[(separator + 1)..])
            .GetProperty(key[..separator].ToLowerInvariant());
    }

    private static JsonElement Serialized<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Web);
}
