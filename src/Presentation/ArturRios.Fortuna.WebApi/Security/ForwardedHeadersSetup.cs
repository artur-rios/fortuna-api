using ArturRios.Fortuna.WebApi.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Trusts <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> only from the configured reverse
/// proxies. Without it every request behind a proxy carries the proxy's address, so the per-client
/// rate limiter would put all callers in one partition and lock everyone out together.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static void Configure(ForwardedHeadersOptions forwarded, FortunaOptions options)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The loopback defaults stay trusted, so a proxy on the same host keeps working unconfigured.
        foreach (var network in options.ForwardedKnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(network);
        }

        foreach (var proxy in options.ForwardedKnownProxies)
        {
            forwarded.KnownProxies.Add(proxy);
        }
    }
}
