using System.Text.RegularExpressions;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Keeps a new layer from answering a status the document does not list. docs/openapi/fortuna.json's error
///     responses are worked out from the layers below: the request pipeline in its order, and the filters the
///     actions carry. A middleware or a filter added to either fails here until ErrorResponseOperationFilter
///     documents what it answers and the list below names it.
/// </summary>
public sealed partial class OpenApiLayerAuditTests
{
    // UseFortunaPipeline, in order, with what each layer answers and where the document lists it.
    private static readonly string[] Pipeline =
    [
        "UseExceptionHandler", // 500, DataOutput (ErrorSource.Exception, RequestBody)
        "UseForwardedHeaders", // nothing
        "UsePrometheusMetrics", // nothing on the API's port: /metrics is on its own port
        "UseMiddleware<TraceActivityMiddleware>", // nothing
        "UseSwagger", // nothing: the document itself, outside Production
        "UseSwaggerUI", // nothing: its page, outside Production
        "UseSerilogRequestLogging", // nothing
        "UseRateLimiter", // 429 with no body (ErrorSource.RateLimit)
        "UseAuthentication", // 401 challenge (ErrorSource.Challenge, through UseAuthorization)
        "UseMiddleware<AuthenticatedActorMiddleware>", // 401 with no body (ErrorSource.Identity)
        "UseMiddleware<UserProfileProvisioningMiddleware>", // 401 with no body (ErrorSource.Identity)
        "UseAuthorization", // 401 challenge (ErrorSource.Challenge)
        "MapControllers" // MVC: the filters below, model binding, the handlers
    ];

    // Every filter an action or controller carries, with what it answers.
    private static readonly Type[] Filters =
    [
        typeof(ApiControllerAttribute), // 400 from model state, problem details for a client error (415, 403)
        typeof(ConsumesAttribute), // 415 with no body (ErrorSource.Consumes)
        typeof(ProducesResponseTypeAttribute), // nothing: metadata
        typeof(RoleRequirementAttribute), // 403 ProcessOutput (ErrorSource.Role)
        typeof(InstallationAdministratorRequirementAttribute), // 403 problem details
        typeof(UploadLimitAttribute), // 400 DataOutput (ErrorSource.UploadLimit)
        typeof(AllowedQueryAttribute) // 400 with the action's envelope (ErrorSource.AllowedQuery)
    ];

    [UnitFact]
    public void GivenTheRequestPipeline_WhenRead_ThenEveryLayerIsOneTheDocumentAccountsFor()
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "src", "Presentation", "ArturRios.Fortuna.WebApi",
            "Extensions", "FortunaPipelineExtensions.cs"));

        var layers = Layer().Matches(source).Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(Pipeline, layers);
    }

    [UnitFact]
    public void GivenTheActionsFilters_WhenRead_ThenEachIsOneTheDocumentAccountsFor()
    {
        var carried = OpenApiDocumentAccuracyTests.Actions()
            .SelectMany(action => action.Value.GetCustomAttributes(inherit: true)
                .Concat(action.Value.DeclaringType!.GetCustomAttributes(inherit: true)))
            .Where(attribute => attribute is IFilterMetadata or IFilterFactory)
            .Select(attribute => attribute.GetType())
            .Distinct()
            .ToArray();

        Assert.NotEmpty(carried);
        Assert.All(carried, filter => Assert.Contains(filter, Filters));
    }

    [UnitFact]
    public void GivenTheServices_WhenRead_ThenNoGlobalFilterOrStatusPageAnswersBesideTheDocumentedOnes()
    {
        var services = File.ReadAllText(Path.Combine(Repository(), "src", "Presentation", "ArturRios.Fortuna.WebApi",
            "Extensions", "FortunaWebApiServiceCollectionExtensions.cs"));
        var pipeline = File.ReadAllText(Path.Combine(Repository(), "src", "Presentation", "ArturRios.Fortuna.WebApi",
            "Extensions", "FortunaPipelineExtensions.cs"));

        // A global MVC filter, or status code pages, would answer for every action without carrying an attribute.
        Assert.DoesNotContain("Filters.Add", services, StringComparison.Ordinal);
        Assert.DoesNotContain("UseStatusCodePages", pipeline, StringComparison.Ordinal);
        Assert.True(typeof(FortunaController).GetCustomAttributes(inherit: true).OfType<IFilterMetadata>().All(
            filter => Filters.Contains(filter.GetType())));
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "docs", "openapi", "fortuna.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [GeneratedRegex(@"app\.((?:Use\w+|Map\w+)(?:<\w+>)?)\(")]
    private static partial Regex Layer();
}
