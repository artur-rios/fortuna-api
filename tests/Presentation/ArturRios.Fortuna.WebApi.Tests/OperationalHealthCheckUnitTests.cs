using System.Net;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Util.Test.Attributes;
using Moq;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class OperationalHealthCheckUnitTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData(true, OperationalHealthStatus.Healthy)]
    [InlineData(false, OperationalHealthStatus.Unhealthy)]
    public async Task GivenStorageAvailability_WhenChecked_ThenRequiredStatusIsReturned(
        bool available,
        OperationalHealthStatus expected)
    {
        var storage = new Mock<IAttachmentStore>();
        storage.Setup(item => item.IsHealthyAsync(CancellationToken.None))
            .ReturnsAsync(available);

        var result = await new AttachmentStorageOperationalHealthCheck(storage.Object)
            .CheckAsync(CancellationToken.None);

        Assert.Equal("AttachmentStorage", result.Name);
        Assert.Equal(expected, result.Status);
        Assert.True(result.IsRequired);
    }

    [UnitTheory]
    [InlineData(299, OperationalHealthStatus.Healthy)]
    [InlineData(301, OperationalHealthStatus.Unhealthy)]
    public async Task GivenOldestPendingAge_WhenChecked_ThenRunnerHealthAndMetricsAreReturned(
        int ageSeconds,
        OperationalHealthStatus expected)
    {
        var queue = new Mock<IBackgroundJobQueue>();
        queue.SetupGet(item => item.Depth).Returns(4);
        var jobs = new Mock<IBackgroundJobHealthReader>();
        jobs.Setup(item => item.GetOldestPendingAtAsync(CancellationToken.None))
            .ReturnsAsync(Now.AddSeconds(-ageSeconds));
        var check = new JobRunnerOperationalHealthCheck(
            queue.Object,
            jobs.Object,
            new OperationalHealthOptions(TimeSpan.FromSeconds(300)),
            new FixedTimeProvider());

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(4, result.QueueDepth);
        Assert.Equal(ageSeconds, result.OldestPendingSeconds);
        Assert.True(result.IsRequired);
    }

    [UnitFact]
    public async Task GivenUnconfiguredExternalService_WhenChecked_ThenItDoesNotCallNetwork()
    {
        var calls = 0;
        using var client = Client(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await new ExternalServiceOperationalHealthCheck(
            "Aggregator", configured: false, client).CheckAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.NotConfigured, result.Status);
        Assert.False(result.IsRequired);
        Assert.Equal(0, calls);
    }

    [UnitFact]
    public async Task GivenReachableExternalService_WhenChecked_ThenItIsHealthy()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await new ExternalServiceOperationalHealthCheck(
            "Aggregator", configured: true, client).CheckAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Healthy, result.Status);
    }

    [UnitFact]
    public async Task GivenUnreachableExternalService_WhenChecked_ThenItIsDegraded()
    {
        using var client = Client(_ => throw new HttpRequestException("offline"));

        var result = await new ExternalServiceOperationalHealthCheck(
            "Aggregator", configured: true, client).CheckAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Degraded, result.Status);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://health.example.test/")
        };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
