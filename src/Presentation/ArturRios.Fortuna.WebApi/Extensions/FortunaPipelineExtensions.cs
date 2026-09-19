using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.Security;
using Serilog;
using Serilog.Events;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaPipelineExtensions
{
    /// <summary>
    ///     Builds the request pipeline. Development keeps the developer exception page; elsewhere an
    ///     unhandled exception becomes a generic DataOutput 500 without internals. Forwarded headers
    ///     are applied before anything that reads the client address (request logging, the
    ///     per-client rate limiter).
    /// </summary>
    public static WebApplication UseFortunaPipeline(this WebApplication app, FortunaOptions options)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(handler => handler.Run(ApiErrorResponses.WriteUnexpectedErrorAsync));
        }

        app.UseForwardedHeaders();
        app.UsePrometheusMetrics(options.MetricsPort);
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging(logging =>
            logging.GetLevel = (_, _, _) => LogEventLevel.Information);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<AuthenticatedActorMiddleware>();
        app.UseMiddleware<UserProfileProvisioningMiddleware>();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
