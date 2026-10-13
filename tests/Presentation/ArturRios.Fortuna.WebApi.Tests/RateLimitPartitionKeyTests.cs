using System.Net;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class RateLimitPartitionKeyTests
{
    [UnitFact]
    public void GivenNoAddress_WhenKeying_ThenUnknown() =>
        Assert.Equal("unknown", RateLimitPartitionKey.For(null));

    [UnitFact]
    public void GivenIpv4_WhenKeying_ThenItsString() =>
        Assert.Equal("203.0.113.9", RateLimitPartitionKey.For(IPAddress.Parse("203.0.113.9")));

    [UnitFact]
    public void GivenIpv4MappedIpv6_WhenKeying_ThenTheIpv4String() =>
        Assert.Equal("203.0.113.9", RateLimitPartitionKey.For(IPAddress.Parse("::ffff:203.0.113.9")));

    [UnitFact]
    public void GivenTwoIpv6AddressesInTheSame64_WhenKeying_ThenTheKeyIsTheSame()
    {
        var first = RateLimitPartitionKey.For(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        var second = RateLimitPartitionKey.For(IPAddress.Parse("2001:db8:1:2::1"));

        Assert.Equal(first, second);
        Assert.EndsWith("/64", first, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenIpv6AddressesInDifferent64s_WhenKeying_ThenTheKeysDiffer()
    {
        var first = RateLimitPartitionKey.For(IPAddress.Parse("2001:db8:1:2::1"));
        var second = RateLimitPartitionKey.For(IPAddress.Parse("2001:db8:1:3::1"));

        Assert.NotEqual(first, second);
    }
}
