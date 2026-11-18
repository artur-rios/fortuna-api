using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.WebApi.Middleware;
using Serilog;
using Serilog.Events;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaPipelineExtensions
{
    /// <summary>
    ///     Builds the request pipeline. Development keeps the developer exception page; elsewhere an
    ///     unhandled exception becomes a generic DataOutput 500 without internals, and a request body Kestrel
    ///     refused a DataOutput with Kestrel's status (413, 408, 400). Forwarded headers
    ///     are applied before anything that reads the client address (the trace middleware's
    ///     per-request log entry, request logging, the per-client rate limiter).
    /// </summary>
    public static WebApplication UseFortunaPipeline(this WebApplication app, FortunaOptions options)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                ExceptionHandler = ApiErrorResponses.WriteErrorAsync,
                // A request Kestrel refused is the client's mistake: answered with its own status and logged as a
                // warning by the handler, not reported as an unhandled exception with its stack trace.
                SuppressDiagnosticsCallback = context => ApiErrorResponses.IsClientFault(context.Exception)
            });
        }

        app.UseForwardedHeaders();
        app.UsePrometheusMetrics(options.MetricsPort);
        // Logs the start of every API request with its trace id and client IP address, and tags the
        // address on the request's activity as client.address (see AddFortunaWebApi). After forwarded
        // headers, so behind a trusted proxy the address is the caller's rather than the proxy's;
        // after the private metrics listener, so a scrape stays unlogged; ahead of request logging,
        // rate limiting and authentication, so a request they refuse is still recorded.
        app.UseMiddleware<TraceActivityMiddleware>();
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
