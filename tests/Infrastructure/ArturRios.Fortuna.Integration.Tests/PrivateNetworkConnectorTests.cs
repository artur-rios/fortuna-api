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
        // The public address comes first and is unroutable: trying it would hang past the deadline.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await using var stream = await PrivateNetworkConnector.ConnectAsync(
            new DnsEndPoint("heimdall-api.production", port),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.10"), IPAddress.Loopback }),
            deadline.Token);
        using var connection = await accepted;

        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)connection.Client.RemoteEndPoint!).Address);
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
