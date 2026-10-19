using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace ArturRios.Fortuna.Integration.Security;

/// <summary>
///     The connect step of the plain-HTTP transport to Heimdall: a connection is opened only to a
///     private address, decided when the connection is made, so a hostname that later resolves to a
///     public address never receives the credentials Fortuna relays.
/// </summary>
public static class PrivateNetworkConnector
{
    public const string RefusedMessage =
        "Plain HTTP to Heimdall goes only to a private address; the host resolved to none.";

    // RFC 1918, IPv4 loopback, and the IPv6 unique-local range (fc00::/7).
    private static readonly IPNetwork[] PrivateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("fc00::/7")
    ];

    /// <summary>
    ///     Whether the address is on a private network: 10/8, 172.16/12, 192.168/16, 127/8, ::1 or
    ///     fc00::/7. An IPv4-mapped IPv6 address is judged by its IPv4 address. Link-local addresses
    ///     (169.254/16, fe80::/10) are not private: cloud metadata services answer there.
    /// </summary>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var normalized = Normalize(address);

        return IPAddress.IPv6Loopback.Equals(normalized) ||
               PrivateNetworks.Any(network => network.Contains(normalized));
    }

    /// <summary>
    ///     A <see cref="SocketsHttpHandler.ConnectCallback" />: resolves the host, keeps its private
    ///     addresses and connects to the first that accepts; with none, refuses before any byte is sent.
    /// </summary>
    public static ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return ConnectAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, cancellationToken);
    }

    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        // An IP literal is its own address; brackets around an IPv6 literal are dropped first.
        var host = endPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await resolve(endPoint.Host, cancellationToken).ConfigureAwait(false);
        var candidates = addresses.Where(IsPrivate).Select(Normalize).Distinct().ToArray();
        if (candidates.Length == 0)
        {
            throw new HttpRequestException(RefusedMessage);
        }

        Exception? lastFailure = null;
        foreach (var address in candidates)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken)
                    .ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastFailure = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        ExceptionDispatchInfo.Throw(lastFailure!);

        return null!;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
