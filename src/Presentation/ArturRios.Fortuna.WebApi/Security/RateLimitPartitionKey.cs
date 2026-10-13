using System.Net;
using System.Net.Sockets;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// The partition a client's requests are counted in by Fortuna's own anonymous-auth rate limiter:
/// the whole address for IPv4 and the <c>/64</c> it belongs to for IPv6. It is the same rule Heimdall
/// applies, so the two limiters agree on who one caller is.
/// </summary>
/// <remarks>
/// A /64 is what one IPv6 subscriber is routinely handed and every address inside it is theirs to
/// use, so counting per address would let a single caller give each request a fresh partition and
/// never meet the limit. An IPv4-mapped IPv6 address is one IPv4 caller and is counted as that
/// address.
/// </remarks>
public static class RateLimitPartitionKey
{
    public const string Unknown = "unknown";

    public static string For(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);

        return $"{new IPAddress(bytes)}/64";
    }
}
