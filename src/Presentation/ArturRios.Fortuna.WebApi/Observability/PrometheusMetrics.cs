using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace ArturRios.Fortuna.WebApi.Observability;

/// <summary>
/// Publishes OpenTelemetry metrics in the Prometheus text format on a private listener. The API
/// runs behind a reverse proxy that forwards the caller's Host header, so the scrape endpoint is
/// selected by the local port the connection arrived on rather than by any request header.
/// </summary>
public static class PrometheusMetrics
{
    public const string ServiceName = "fortuna-api";
    public const string ScrapePath = "/metrics";

    internal static readonly string[] BuiltInMeters =
    [
        "System.Runtime",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.EntityFrameworkCore",
        "Npgsql"
    ];

    public static IServiceCollection AddPrometheusMetrics(this IServiceCollection services, int metricsPort)
    {
        if (metricsPort <= 0)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(BuiltInMeters)
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Must run before authentication, authorization, rate limiting and routing so none of them
    /// apply to a scrape; the endpoint is middleware, so it never reaches the OpenAPI document.
    /// </summary>
    public static IApplicationBuilder UsePrometheusMetrics(this IApplicationBuilder app, int metricsPort) =>
        metricsPort <= 0
            ? app
            : app.UseOpenTelemetryPrometheusScrapingEndpoint(context => IsScrapeRequest(context, metricsPort));

    public static bool IsScrapeRequest(HttpContext context, int metricsPort) =>
        IsScrapeRequest(context.Request.Path, context.Connection.LocalPort, metricsPort);

    public static bool IsScrapeRequest(PathString path, int localPort, int metricsPort) =>
        metricsPort > 0 &&
        localPort == metricsPort &&
        path.Equals(ScrapePath, StringComparison.OrdinalIgnoreCase);
}
