using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class PrometheusMetricsUnitTests
{
    private const int MetricsPort = 9464;
    private const int PublicPort = 8080;

    [UnitTheory]
    [InlineData("/metrics", MetricsPort)]
    [InlineData("/METRICS", MetricsPort)]
    public void GivenMetricsPathOnPrivatePort_WhenPredicateEvaluated_ThenRequestIsScraped(
        string path,
        int localPort)
    {
        Assert.True(PrometheusMetrics.IsScrapeRequest(Context(path, localPort), MetricsPort));
    }

    [UnitTheory]
    [InlineData("/metrics", PublicPort)]
    [InlineData("/metrics", 0)]
    [InlineData("/healthcheck", MetricsPort)]
    [InlineData("/metrics/", MetricsPort)]
    [InlineData("/api/metrics", MetricsPort)]
    [InlineData("/", MetricsPort)]
    public void GivenPublicPortOrOtherPath_WhenPredicateEvaluated_ThenRequestIsNotScraped(
        string path,
        int localPort)
    {
        Assert.False(PrometheusMetrics.IsScrapeRequest(Context(path, localPort), MetricsPort));
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenExporterDisabled_WhenPredicateEvaluated_ThenNoRequestIsScraped(int metricsPort)
    {
        Assert.False(PrometheusMetrics.IsScrapeRequest(Context("/metrics", 0), metricsPort));
        Assert.False(PrometheusMetrics.IsScrapeRequest(Context("/metrics", MetricsPort), metricsPort));
    }

    [UnitFact]
    public void GivenForgedHostHeader_WhenPredicateEvaluated_ThenOnlyTheLocalPortDecides()
    {
        var context = Context("/metrics", PublicPort);
        context.Request.Host = new HostString("localhost", MetricsPort);

        Assert.False(PrometheusMetrics.IsScrapeRequest(context, MetricsPort));
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(MetricsPort)]
    public void GivenMetricsPort_WhenServicesRegistered_ThenMeterProviderExistsOnlyWhenEnabled(
        int metricsPort)
    {
        var services = new ServiceCollection();

        services.AddPrometheusMetrics(metricsPort);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(metricsPort > 0, provider.GetService<MeterProvider>() is not null);
    }

    [UnitTheory]
    [InlineData(MetricsPort, MetricsPort, true)]
    [InlineData(PublicPort, MetricsPort, false)]
    [InlineData(0, MetricsPort, false)]
    [InlineData(0, 0, false)]
    public void GivenLocalPort_WhenListenerEvaluated_ThenOnlyTheMetricsPortIsPrivate(
        int localPort,
        int metricsPort,
        bool expected)
    {
        Assert.Equal(expected, PrometheusMetrics.IsMetricsListenerRequest(localPort, metricsPort));
    }

    [UnitTheory]
    [InlineData("/metrics", MetricsPort, StatusCodes.Status200OK)]
    [InlineData("/healthcheck", MetricsPort, StatusCodes.Status404NotFound)]
    [InlineData("/api/accounts", MetricsPort, StatusCodes.Status404NotFound)]
    [InlineData("/", MetricsPort, StatusCodes.Status404NotFound)]
    [InlineData("/healthcheck", PublicPort, StatusCodes.Status202Accepted)]
    [InlineData("/metrics", PublicPort, StatusCodes.Status202Accepted)]
    public async Task GivenPipeline_WhenRequestArrives_ThenMetricsListenerServesOnlyTheScrape(
        string path,
        int localPort,
        int expectedStatus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrometheusMetrics(MetricsPort);
        await using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UsePrometheusMetrics(MetricsPort);
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;

            return Task.CompletedTask;
        });
        var pipeline = app.Build();
        var context = Context(path, localPort);
        context.RequestServices = provider;
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [UnitFact]
    public async Task GivenExporterDisabled_WhenRequestArrivesOnFormerMetricsPort_ThenApiServesIt()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UsePrometheusMetrics(0);
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;

            return Task.CompletedTask;
        });
        var context = Context("/healthcheck", MetricsPort);

        await app.Build()(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string path, int localPort)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.LocalPort = localPort;

        return context;
    }
}
