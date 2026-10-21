using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class PrivateNetworkConnectorTests
{
    [UnitTheory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:172.18.0.7", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("203.0.113.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    public void GivenAddress_WhenClassified_ThenOnlyPrivateRangesArePrivate(string address, bool expected)
    {
        Assert.Equal(expected, PrivateNetworkConnector.IsPrivate(IPAddress.Parse(address)));
    }

    [UnitFact]
    public async Task GivenPlainHttpToPublicAddress_WhenSending_ThenNoConnectionIsMade()
    {
        using var client = PlainHttpClient();
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(new Uri("http://203.0.113.10/")));
        stopwatch.Stop();

        Assert.Contains("private address", exception.Message, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"The refusal took {stopwatch.Elapsed}.");
    }

    [UnitFact]
    public async Task GivenPlainHttpToLoopback_WhenSending_ThenTheServerAnswers()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = AnswerOnceAsync(listener, "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n");
        using var client = PlainHttpClient();

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"));
        await server;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [UnitFact]
    public async Task GivenHostResolvingToPublicAndPrivateAddresses_WhenConnecting_ThenOnlyThePrivateOneIsUsed()
    {
        // The public address comes first; the connect seam records every endpoint actually dialed.
        const int port = 8080;
        var dialed = new List<IPEndPoint>();

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("heimdall-api.production", port),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.10"), IPAddress.Loopback }),
            (address, _) =>
            {
                dialed.Add(address);

                return ValueTask.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);

        Assert.Equal([new IPEndPoint(IPAddress.Loopback, port)], dialed);
    }

    [UnitFact]
    public async Task GivenHostResolvingOnlyToPublicAddresses_WhenConnecting_ThenItIsRefused()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await PrivateNetworkConnector.ConnectAsync(
                new DnsEndPoint("heimdall.example.test", 80),
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("2001:db8::1") }),
                CancellationToken.None));

        Assert.Equal(PrivateNetworkConnector.RefusedMessage, exception.Message);
    }

    [UnitFact]
    public async Task GivenFirstPrivateAddressRefuses_WhenConnecting_ThenTheNextPrivateOneIsTried()
    {
        // The listener holds 127.0.0.1 only, so 127.0.0.2 refuses the connection at once.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("heimdall-api.production", port),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback }),
            CancellationToken.None);
        using var connection = await accepted;

        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)connection.Client.RemoteEndPoint!).Address);
    }

    [UnitFact]
    public async Task GivenEveryPrivateAddressRefuses_WhenConnecting_ThenTheConnectionFails()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        await Assert.ThrowsAsync<SocketException>(async () =>
            await PrivateNetworkConnector.ConnectAsync(
                new DnsEndPoint("heimdall-api.production", port),
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback }),
                CancellationToken.None));
    }

    [UnitFact]
    public async Task GivenFirstPrivateAddressNeverAnswers_WhenConnecting_ThenTheNextIsTriedOnceItsAttemptTimesOut()
    {
        // The first address swallows the connection attempt; only the attempt's own limit ends it.
        const int port = 8080;
        var silent = IPAddress.Parse("10.0.0.7");
        var dialed = new List<IPEndPoint>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("heimdall-api.production", port),
            (_, _) => Task.FromResult(new[] { silent, IPAddress.Loopback }),
            async (address, token) =>
            {
                dialed.Add(address);
                if (address.Address.Equals(silent))
                {
                    await Task.Delay(Timeout.Infinite, token);
                }

                return new MemoryStream();
            },
            TimeSpan.FromMilliseconds(200),
            guard.Token);
        stopwatch.Stop();

        Assert.Equal([new IPEndPoint(silent, port), new IPEndPoint(IPAddress.Loopback, port)], dialed);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The fallback took {stopwatch.Elapsed}.");
    }

    [UnitFact]
    public async Task GivenEveryPrivateAddressNeverAnswers_WhenConnecting_ThenTheConnectionTimesOut()
    {
        // The guard only bounds the test: the attempts' own limit must end them long before it.
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dialed = new List<IPEndPoint>();

        var exception = await Assert.ThrowsAsync<SocketException>(async () =>
            await PrivateNetworkConnector.ConnectAsync(
                new DnsEndPoint("heimdall-api.production", 8080),
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.7"), IPAddress.Parse("10.0.0.8") }),
                async (address, token) =>
                {
                    dialed.Add(address);
                    await Task.Delay(Timeout.Infinite, token);

                    return new MemoryStream();
                },
                TimeSpan.FromMilliseconds(100),
                guard.Token));

        Assert.Equal(SocketError.TimedOut, exception.SocketErrorCode);
        Assert.Equal(2, dialed.Count);
    }

    [UnitFact]
    public async Task GivenTheRequestIsCancelledDuringAnAttempt_WhenConnecting_ThenNoOtherAddressIsTried()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var dialed = new List<IPEndPoint>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PrivateNetworkConnector.ConnectAsync(
                new DnsEndPoint("heimdall-api.production", 8080),
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.7"), IPAddress.Loopback }),
                async (address, token) =>
                {
                    dialed.Add(address);
                    await Task.Delay(Timeout.Infinite, token);

                    return new MemoryStream();
                },
                TimeSpan.FromSeconds(30),
                cancellation.Token));

        Assert.Equal([new IPEndPoint(IPAddress.Parse("10.0.0.7"), 8080)], dialed);
    }

    [UnitFact]
    public void GivenTheAttemptTimeout_WhenComparedWithTheHeimdallClientTimeout_ThenALaterAddressStillHasTime()
    {
        // The Heimdall client gives a whole request ten seconds.
        Assert.InRange(PrivateNetworkConnector.AttemptTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    [UnitFact]
    public async Task GivenBracketedIpv6LoopbackLiteral_WhenConnecting_ThenItIsDialedWithoutBeingResolved()
    {
        // SocketsHttpHandler hands the callback "[::1]" for http://[::1]:port/; read as a host name, it would
        // go to the resolver, which this test refuses to be. IPAddress.TryParse reads the bracketed form as
        // well, so this pins the outcome, not the trim before it: dropping the trim keeps it green.
        const int port = 8080;
        var dialed = new List<IPEndPoint>();

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("[::1]", port),
            (host, _) => throw new InvalidOperationException($"'{host}' was resolved."),
            (address, _) =>
            {
                dialed.Add(address);

                return ValueTask.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);

        Assert.Equal([new IPEndPoint(IPAddress.IPv6Loopback, port)], dialed);
    }

    [UnitFact]
    public async Task GivenIpv4MappedPrivateAddress_WhenConnecting_ThenItConnectsOverIpv4()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("heimdall-api.production", port),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("::ffff:127.0.0.1") }),
            CancellationToken.None);
        using var connection = await accepted;

        Assert.True(stream.CanWrite);
    }

    private static HttpClient PlainHttpClient() =>
        new(new SocketsHttpHandler { ConnectCallback = PrivateNetworkConnector.ConnectAsync })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static async Task AnswerOnceAsync(TcpListener listener, string response)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        var stream = connection.GetStream();
        var buffer = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return;
            }

            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        await stream.FlushAsync();
    }
}
