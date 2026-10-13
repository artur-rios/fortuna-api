using System.Net;
using ArturRios.Fortuna.Integration.Security;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Tells Heimdall which end user a request is made for. Heimdall rate-limits anonymous auth by
/// connection address, and every Fortuna user reaches it from Fortuna's own address, so without
/// this the whole instance shares one budget. With a trusted caller key configured, each outgoing
/// request names the address of the user whose request is being served, and Heimdall counts it
/// there; the headers grant nothing else.
/// </summary>
/// <remarks>
/// It lives in the WebApi project because it needs <see cref="IHttpContextAccessor" />, and the
/// Integration project (home of <see cref="HeimdallAuthGateway" />) deliberately has no ASP.NET Core
/// reference. The address is <c>Connection.RemoteIpAddress</c>, which is the client's real address
/// after Fortuna's own <c>UseForwardedHeaders</c> has run with the configured known proxies. The
/// key is never logged. Headers already on the request are always replaced, or removed when nothing
/// is sent, so a value set by anything else can never reach Heimdall.
/// </remarks>
public sealed class HeimdallCallerHeadersHandler(IHttpContextAccessor accessor, string? callerKey)
    : DelegatingHandler
{
    public const string CallerKeyHeader = "X-Heimdall-Caller-Key";
    public const string ClientAddressHeader = "X-Heimdall-Client-Address";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(CallerKeyHeader);
        request.Headers.Remove(ClientAddressHeader);

        var address = accessor.HttpContext?.Connection.RemoteIpAddress;
        if (!string.IsNullOrWhiteSpace(callerKey) && address is not null)
        {
            request.Headers.Add(CallerKeyHeader, callerKey);
            request.Headers.Add(ClientAddressHeader, Normalize(address).ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
