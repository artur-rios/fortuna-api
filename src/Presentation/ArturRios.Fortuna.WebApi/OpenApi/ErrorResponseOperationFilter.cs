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

    /// <summary>The rate limiter under <c>[EnableRateLimiting]</c>: no body, no <c>Retry-After</c>.</summary>
    RateLimit,

    /// <summary>
    ///     The exception handler outside Development (<c>ApiErrorResponses.WriteUnexpectedErrorAsync</c>):
    ///     <c>DataOutput&lt;object?&gt;</c>, written as <c>application/json</c> whatever the request accepts.
    /// </summary>
    Exception
}

/// <summary>
///     Documents every error response an operation can answer, with the body, media types and headers the
///     server really writes for it.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline's sources are read from the action's metadata: a 400 wherever MVC binds a body, form or
///         query value, and the upload limit's 400 under <c>[UploadLimit]</c>; JWT authentication's 401
///         challenge on every operation that is not <c>[AllowAnonymous]</c>, and the identity middlewares'
///         401 on every operation (they refuse a token that validates but names no Fortuna identity, whatever
///         the endpoint); a 403 from <c>[RoleRequirement]</c> and from
///         <c>[InstallationAdministratorRequirement]</c>; a 415 wherever a body is read from the request
///         (problem details for a <c>[FromBody]</c> one, no body for a form under <c>[Consumes]</c>, whose
///         refusal <c>[ApiController]</c> does not map); the rate limiter's 429 under
///         <c>[EnableRateLimiting]</c>; and the exception handler's 500 everywhere.
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

    // Spelled out rather than read from ReasonPhrases, whose wording for a status can change between
    // releases (422 became "Unprocessable Content" in RFC 9110), so the document's text stays put.
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [StatusCodes.Status400BadRequest] = "Bad Request",
        [StatusCodes.Status401Unauthorized] = "Unauthorized",
        [StatusCodes.Status403Forbidden] = "Forbidden",
        [StatusCodes.Status404NotFound] = "Not Found",
        [StatusCodes.Status409Conflict] = "Conflict",
        [StatusCodes.Status415UnsupportedMediaType] = "Unsupported Media Type",
        [StatusCodes.Status422UnprocessableEntity] = "Unprocessable Entity",
        [StatusCodes.Status429TooManyRequests] = "Too Many Requests",
        [StatusCodes.Status500InternalServerError] = "Internal Server Error",
        [StatusCodes.Status503ServiceUnavailable] = "Service Unavailable"
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
        }

        if (!metadata.OfType<DisableRateLimitingAttribute>().Any() &&
            metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            Add(StatusCodes.Status429TooManyRequests, ErrorSource.RateLimit);
        }

        Add(StatusCodes.Status500InternalServerError, ErrorSource.Exception);

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
            or ErrorSource.Consumes)).ToArray();
        if (json.Length > 0)
        {
            var type = json.All(source => source == ErrorSource.Role)
                ? typeof(ProcessOutput)
                : envelope ?? typeof(DataOutput<object?>);
            var schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
            content[ApplicationJson] = new OpenApiMediaType { Schema = schema };
            // The exception handler writes application/json only; every other source is an ObjectResult,
            // negotiated like the action's own answer.
            if (json.Any(source => source != ErrorSource.Exception))
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
        ErrorSource.Exception =>
            "An unexpected failure: `errors` holds a generic message and nothing of the exception. As " +
            "`application/json` only.",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
