using System.Globalization;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>What answers an error status on an operation, in the order its description names them.</summary>
public enum ErrorSource
{
    /// <summary>
    ///     MVC could not bind a body, form or query value: <c>ApiErrorResponses.InvalidModelState</c>'s
    ///     <c>DataOutput&lt;object?&gt;</c>, negotiated as JSON.
    /// </summary>
    Binding,

    /// <summary><c>UploadLimitFilter</c>: a declared length over the limit, <c>DataOutput&lt;object?&gt;</c>.</summary>
    UploadLimit,

    /// <summary>
    ///     <c>[AllowedQuery]</c>: a query parameter the action does not bind, answered with the action's envelope
    ///     shape (<c>PaginatedOutput&lt;object&gt;</c> or <c>DataOutput&lt;object?&gt;</c>).
    /// </summary>
    AllowedQuery,

    /// <summary>The handler, through the controller's status map: the action's envelope, <c>data</c> null.</summary>
    Handler,

    /// <summary>JWT bearer authentication's challenge: no body, <c>WWW-Authenticate: Bearer</c>.</summary>
    Challenge,

    /// <summary><c>AuthenticatedActorMiddleware</c>, <c>UserProfileProvisioningMiddleware</c>: no body.</summary>
    Identity,

    /// <summary><c>[RoleRequirement]</c>'s filter: <c>ProcessOutput</c>, negotiated as JSON.</summary>
    Role,

    /// <summary>
    ///     <c>[InstallationAdministratorRequirement]</c>: a bare 403, which <c>[ApiController]</c> turns into
    ///     problem details (<c>application/problem+json</c>).
    /// </summary>
    InstallationAdministrator,

    /// <summary>
    ///     A <c>[FromBody]</c> body in a media type no input formatter reads: <c>[ApiController]</c>'s problem
    ///     details.
    /// </summary>
    ContentType,

    /// <summary>A form under <c>[Consumes]</c> sent as another media type: <c>ConsumesAttribute</c>, no body.</summary>
    Consumes,

    /// <summary>
    ///     Kestrel refusing a form while MVC reads it (too slow, badly framed, over the form's limits): the form
    ///     value provider's failure, which model binding answers with <c>DataOutput&lt;object?&gt;</c>.
    /// </summary>
    FormBody,

    /// <summary>The rate limiter under <c>[EnableRateLimiting]</c>: no body, no <c>Retry-After</c>.</summary>
    RateLimit,

    /// <summary>
    ///     The exception handler outside Development (<c>ApiErrorResponses.WriteUnexpectedErrorAsync</c>):
    ///     <c>DataOutput&lt;object?&gt;</c>, written as <c>application/json</c> whatever the request accepts.
    /// </summary>
    Exception,

    /// <summary>
    ///     Kestrel refusing a <c>[FromBody]</c> body while the JSON input formatter reads it (over
    ///     <c>MaxRequestBodySize</c>, below <c>MinRequestBodyDataRate</c>, badly chunked): a
    ///     <c>BadHttpRequestException</c> that the body binder passes on, so the exception handler answers it with
    ///     its 500, never Kestrel's own 413, 408 or 400.
    /// </summary>
    RequestBody,

    /// <summary>
    ///     Kestrel refusing the request line or headers before the API sees the request: its own response, with
    ///     <c>Content-Length: 0</c> and no body.
    /// </summary>
    Server,

    /// <summary>Traefik, in front of the API in production: a <c>text/plain</c> reason phrase.</summary>
    Proxy
}

/// <summary>
///     Documents every error response an operation can answer, with the body, media types and headers the
///     server really writes for it.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline's sources are read from the action's metadata: a 400 wherever MVC binds a body, form or
///         query value, the upload limit's 400 under <c>[UploadLimit]</c> and the unknown query parameter's 400
///         under <c>[AllowedQuery]</c>; JWT authentication's 401
///         challenge on every operation that is not <c>[AllowAnonymous]</c>, and the identity middlewares'
///         401 on every operation (they refuse a token that validates but names no Fortuna identity, whatever
///         the endpoint); a 403 from <c>[RoleRequirement]</c> and from
///         <c>[InstallationAdministratorRequirement]</c>; a 415 wherever a body is read from the request
///         (problem details for a <c>[FromBody]</c> one, no body for a form under <c>[Consumes]</c>, whose
///         refusal <c>[ApiController]</c> does not map); the rate limiter's 429 under
///         <c>[EnableRateLimiting]</c>; and the exception handler's 500 everywhere. Below the API: Kestrel's
///         refusals of the request line and headers (400, 408, 414, 431, 505, no body) everywhere, its refusals of
///         a <c>[FromBody]</c> body (which reach the exception handler's 500) and of a form (which reach model
///         binding's 400); and Traefik's 502 and 504 everywhere, as <c>text/plain</c>.
///     </para>
///     <para>
///         The handler's statuses are <see cref="HandlerMessageCatalog" />'s: each message the action, its
///         handlers and the code they reach name, looked up in the status map the action resolves with, and 400,
///         which <c>ToActionResult</c> answers for a failure no map holds, a validator's included; plus the
///         relayed outcomes' statuses where that code relays a Heimdall refusal. A message the code names is
///         taken to be one it can fail with, so it over-approximates where a type serves several handlers, on
///         purpose: a status the document lists and an operation never answers costs a reader a line; one it
///         answers and the document leaves out misleads a client.
///     </para>
///     <para>
///         One schema per media type, the narrowest every body written under it validates against. The JSON
///         bodies are the action's own envelope (<c>DataOutput</c> or <c>PaginatedOutput</c>) when it has one:
///         the handler writes it with a null <c>data</c>, and a <c>ProcessOutput</c> or a
///         <c>DataOutput&lt;object?&gt;</c> with a null <c>data</c> validates against it as well, since no
///         member is required, <c>data</c> may be null and their members are the envelope's. An action without
///         an envelope gets <c>ObjectDataOutput</c>, and a status only <c>[RoleRequirement]</c> answers gets
///         <c>ProcessOutput</c>. Problem details are <c>ProblemDetails</c>, whose extension members (the
///         <c>traceId</c>) it admits.
///     </para>
///     <para>
///         A response API Explorer already lists for a status these sources answer is replaced: it named the
///         same envelope without what answers it. Any other (the detailed health check's 503) is kept.
///     </para>
/// </remarks>
public sealed class ErrorResponseOperationFilter : IOperationFilter
{
    private const string ApplicationJson = "application/json";
    private const string TextJson = "text/json";
    private const string ProblemJson = "application/problem+json";
    private const string TextPlain = "text/plain";

    // Kestrel's own refusals while it parses the request line and headers, before the API sees the request: a
    // malformed request line or header, a missing or invalid Host, an invalid Content-Length or transfer coding
    // (400); headers sent too slowly (408); a request line over MaxRequestLineSize (414); headers over
    // MaxRequestHeadersTotalSize or MaxRequestHeaderCount (431); an HTTP version it does not speak (505). Any
    // request can meet them, so every operation lists them. 405 is left out: Kestrel answers it only for an
    // asterisk-form or authority-form target, which no operation's path is.
    private static readonly int[] ServerStatuses =
    [
        StatusCodes.Status400BadRequest, StatusCodes.Status408RequestTimeout, StatusCodes.Status414UriTooLong,
        StatusCodes.Status431RequestHeaderFieldsTooLarge, StatusCodes.Status505HttpVersionNotsupported
    ];

    // Traefik's own answers for any route it forwards (yggdrasil's stacks/fortuna-api.proxy.yml): the API is
    // unreachable, or the connection was refused or reset (502), or it could not connect in time (504).
    private static readonly int[] ProxyStatuses =
        [StatusCodes.Status502BadGateway, StatusCodes.Status504GatewayTimeout];

    // Spelled out rather than read from ReasonPhrases, whose wording for a status can change between
    // releases (422 became "Unprocessable Content" in RFC 9110), so the document's text stays put.
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [StatusCodes.Status400BadRequest] = "Bad Request",
        [StatusCodes.Status401Unauthorized] = "Unauthorized",
        [StatusCodes.Status403Forbidden] = "Forbidden",
        [StatusCodes.Status404NotFound] = "Not Found",
        [StatusCodes.Status408RequestTimeout] = "Request Timeout",
        [StatusCodes.Status409Conflict] = "Conflict",
        [StatusCodes.Status414UriTooLong] = "URI Too Long",
        [StatusCodes.Status415UnsupportedMediaType] = "Unsupported Media Type",
        [StatusCodes.Status422UnprocessableEntity] = "Unprocessable Entity",
        [StatusCodes.Status429TooManyRequests] = "Too Many Requests",
        [StatusCodes.Status431RequestHeaderFieldsTooLarge] = "Request Header Fields Too Large",
        [StatusCodes.Status500InternalServerError] = "Internal Server Error",
        [StatusCodes.Status502BadGateway] = "Bad Gateway",
        [StatusCodes.Status503ServiceUnavailable] = "Service Unavailable",
        [StatusCodes.Status504GatewayTimeout] = "Gateway Timeout",
        [StatusCodes.Status505HttpVersionNotsupported] = "HTTP Version Not Supported"
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor action)
        {
            return;
        }

        var handler = action.ControllerTypeInfo.IsSubclassOf(typeof(FortunaController))
            ? HandlerMessageCatalog.Statuses(action.ControllerTypeInfo, action.MethodInfo.Name)
            : (Statuses: new HashSet<int>(), RelaysRetryAfter: false);
        var sources = Sources(action, context, handler.Statuses);
        operation.Responses ??= new OpenApiResponses();
        var envelope = Envelope(context);

        foreach (var (status, kinds) in sources)
        {
            operation.Responses[status.ToString(CultureInfo.InvariantCulture)] = Response(
                status,
                kinds,
                envelope,
                context,
                retryAfter: status == StatusCodes.Status429TooManyRequests && handler.RelaysRetryAfter);
        }
    }

    private static SortedDictionary<int, SortedSet<ErrorSource>> Sources(
        ControllerActionDescriptor action,
        OperationFilterContext context,
        IReadOnlySet<int> handlerStatuses)
    {
        var sources = new SortedDictionary<int, SortedSet<ErrorSource>>();

        void Add(int status, ErrorSource source)
        {
            if (!sources.TryGetValue(status, out var set))
            {
                sources[status] = set = [];
            }

            set.Add(source);
        }

        var metadata = action.EndpointMetadata;
        var parameters = context.ApiDescription.ParameterDescriptions;
        if (parameters.Any(parameter => parameter.Source is { IsFromRequest: true } source &&
                                        source != BindingSource.Path))
        {
            Add(StatusCodes.Status400BadRequest, ErrorSource.Binding);
        }

        if (metadata.OfType<UploadLimitAttribute>().Any())
        {
            Add(StatusCodes.Status400BadRequest, ErrorSource.UploadLimit);
        }

        if (metadata.OfType<AllowedQueryAttribute>().Any())
        {
            Add(StatusCodes.Status400BadRequest, ErrorSource.AllowedQuery);
        }

        foreach (var status in handlerStatuses)
        {
            Add(status, ErrorSource.Handler);
        }

        if (!metadata.OfType<IAllowAnonymous>().Any())
        {
            Add(StatusCodes.Status401Unauthorized, ErrorSource.Challenge);
        }

        Add(StatusCodes.Status401Unauthorized, ErrorSource.Identity);

        if (metadata.OfType<RoleRequirementAttribute>().Any())
        {
            Add(StatusCodes.Status403Forbidden, ErrorSource.Role);
        }

        if (metadata.OfType<InstallationAdministratorRequirementAttribute>().Any())
        {
            Add(StatusCodes.Status403Forbidden, ErrorSource.InstallationAdministrator);
        }

        if (parameters.Any(parameter => parameter.Source == BindingSource.Body))
        {
            Add(StatusCodes.Status415UnsupportedMediaType, ErrorSource.ContentType);
        }

        if (metadata.OfType<ConsumesAttribute>().Any())
        {
            Add(StatusCodes.Status415UnsupportedMediaType, ErrorSource.Consumes);
            Add(StatusCodes.Status400BadRequest, ErrorSource.FormBody);
        }

        if (!metadata.OfType<DisableRateLimitingAttribute>().Any() &&
            metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            Add(StatusCodes.Status429TooManyRequests, ErrorSource.RateLimit);
        }

        Add(StatusCodes.Status500InternalServerError, ErrorSource.Exception);

        if (parameters.Any(parameter => parameter.Source == BindingSource.Body))
        {
            Add(StatusCodes.Status500InternalServerError, ErrorSource.RequestBody);
        }

        foreach (var status in ServerStatuses)
        {
            Add(status, ErrorSource.Server);
        }

        foreach (var status in ProxyStatuses)
        {
            Add(status, ErrorSource.Proxy);
        }

        return sources;
    }

    /// <summary>The action's envelope: the <c>DataOutput</c> or <c>PaginatedOutput</c> its success answers.</summary>
    private static Type? Envelope(OperationFilterContext context) =>
        context.ApiDescription.SupportedResponseTypes
            .Where(response => response.StatusCode is >= 200 and < 300 && response.Type is not null)
            .OrderBy(response => response.StatusCode)
            .Select(response => response.Type!)
            .FirstOrDefault(IsEnvelope);

    private static bool IsEnvelope(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                (current.GetGenericTypeDefinition() == typeof(DataOutput<>) ||
                 current.GetGenericTypeDefinition() == typeof(PaginatedOutput<>)))
            {
                return true;
            }
        }

        return false;
    }

    private static OpenApiResponse Response(
        int status,
        SortedSet<ErrorSource> sources,
        Type? envelope,
        OperationFilterContext context,
        bool retryAfter)
    {
        var content = new Dictionary<string, IOpenApiMediaType>();
        var json = sources.Where(source => source is not (ErrorSource.Challenge or ErrorSource.Identity
            or ErrorSource.RateLimit or ErrorSource.InstallationAdministrator or ErrorSource.ContentType
            or ErrorSource.Consumes or ErrorSource.Server or ErrorSource.Proxy)).ToArray();
        if (json.Length > 0)
        {
            var type = json.All(source => source == ErrorSource.Role)
                ? typeof(ProcessOutput)
                : envelope ?? typeof(DataOutput<object?>);
            var schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
            content[ApplicationJson] = new OpenApiMediaType { Schema = schema };
            // The exception handler writes application/json only; every other source is an ObjectResult,
            // negotiated like the action's own answer.
            if (json.Any(source => source is not (ErrorSource.Exception or ErrorSource.RequestBody)))
            {
                content[TextJson] = new OpenApiMediaType { Schema = schema };
            }
        }

        if (sources.Contains(ErrorSource.InstallationAdministrator) || sources.Contains(ErrorSource.ContentType))
        {
            content[ProblemJson] = new OpenApiMediaType
            {
                Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository)
            };
        }

        if (sources.Contains(ErrorSource.Proxy))
        {
            content[TextPlain] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        }

        var headers = new Dictionary<string, IOpenApiHeader>();
        if (sources.Contains(ErrorSource.Challenge))
        {
            headers[HeaderNames.WWWAuthenticate] = new OpenApiHeader
            {
                Description = "Sent with the challenge: `Bearer`, and the token's fault (`error=\"invalid_token\"`) " +
                              "when one was sent.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            };
        }

        if (retryAfter)
        {
            headers[HeaderNames.RetryAfter] = new OpenApiHeader
            {
                Description = "Seconds to wait before trying again, when Heimdall refused the request and said so.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
            };
        }

        return new OpenApiResponse
        {
            Description = $"{Reason(status)}. {string.Join(" ", sources.Select(source => Sentence(status, source)))}",
            Headers = headers.Count > 0 ? headers : null,
            Content = content.Count > 0 ? content : null
        };
    }

    private static string Reason(int status) =>
        Reasons.TryGetValue(status, out var reason) ? reason : status.ToString(CultureInfo.InvariantCulture);

    private static string Sentence(int status, ErrorSource source) => source switch
    {
        ErrorSource.Binding =>
            "The request could not be bound: a body, form or query value is missing or malformed; `errors` names it.",
        ErrorSource.UploadLimit => "The upload's declared length is over the endpoint's limit.",
        ErrorSource.AllowedQuery => "A query parameter the operation does not read was sent; `errors` names it.",
        ErrorSource.Handler when status == StatusCodes.Status400BadRequest =>
            "The handler refused the request: its input is invalid, or it failed with a message the " +
            "controller's status map answers with 400 or does not hold.",
        ErrorSource.Handler =>
            "The handler refused the request with a message the controller's status map answers with this status.",
        ErrorSource.Challenge =>
            "No valid bearer token: none was sent, or it is malformed, expired or not signed by a trusted key. " +
            "No body.",
        ErrorSource.Identity =>
            "A token that validates names no Fortuna identity, or a Heimdall token carries no display name. No body.",
        ErrorSource.Role => "The caller's role is not one the operation admits.",
        ErrorSource.InstallationAdministrator =>
            "The caller does not administer the installation: problem details, as `application/problem+json`.",
        ErrorSource.ContentType =>
            "The request body's Content-Type is not one the operation reads: problem details, as " +
            "`application/problem+json`.",
        ErrorSource.Consumes => "The request is not the form the operation reads (`multipart/form-data`). No body.",
        ErrorSource.RateLimit =>
            "The caller's address has spent this group of operations' budget (10 a minute). No body.",
        ErrorSource.FormBody =>
            "The form could not be read: it arrived slower than the minimum data rate, was badly framed or chunked, " +
            "or is over the form's limits; `errors` says which.",
        ErrorSource.Exception =>
            "An unexpected failure: `errors` holds a generic message and nothing of the exception. As " +
            "`application/json` only. A failure that escapes the exception handler itself (in the code that runs " +
            "ahead of it, or while it writes) is answered by Kestrel with this status and no body.",
        ErrorSource.RequestBody =>
            "Kestrel refused the request body as the action read it: it is over the maximum request body size " +
            "(30,000,000 bytes by default), arrived slower than the minimum data rate, or was badly chunked. The " +
            "exception handler answers that with this 500, not with Kestrel's own 413, 408 or 400.",
        ErrorSource.Server => status switch
        {
            StatusCodes.Status408RequestTimeout =>
                "Kestrel refused the request before the API saw it: its headers arrived too slowly. No body.",
            StatusCodes.Status414UriTooLong =>
                "Kestrel refused the request before the API saw it: its request line is longer than the limit " +
                "(8 KB by default). No body.",
            StatusCodes.Status431RequestHeaderFieldsTooLarge =>
                "Kestrel refused the request before the API saw it: its headers are larger than the limit " +
                "(32 KB, or 100 headers, by default). No body.",
            StatusCodes.Status505HttpVersionNotsupported =>
                "Kestrel refused the request before the API saw it: it names an HTTP version Kestrel does not " +
                "speak. No body.",
            _ =>
                "Kestrel refused the request before the API saw it: a malformed request line or header, a missing " +
                "or invalid Host, or an invalid Content-Length or transfer coding. No body."
        },
        ErrorSource.Proxy => status == StatusCodes.Status502BadGateway
            ? "Traefik, in front of the API where it is deployed behind it, could not reach the API, or the " +
              "connection to it was refused or reset: `text/plain`, \"Bad Gateway\"."
            : "Traefik, in front of the API where it is deployed behind it, could not connect to the API in time " +
              "(30 s by default): `text/plain`, \"Gateway Timeout\".",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
