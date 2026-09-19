using ArturRios.Fortuna.Shared.Health;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class OperationalHealthEvaluatorTests
{
    [UnitFact]
    public async Task GivenHealthyAndUnconfiguredServices_WhenEvaluated_ThenAggregateIsHealthy()
    {
        var evaluator = new OperationalHealthEvaluator([
            Check("Database", OperationalHealthStatus.Healthy, required: true),
            Check("Aggregator", OperationalHealthStatus.NotConfigured, required: false)
        ]);

        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Healthy, result.Status);
        Assert.Equal(2, result.Services.Count);
    }

    [UnitFact]
    public async Task GivenOnlyOptionalServiceDown_WhenEvaluated_ThenAggregateIsDegraded()
    {
        var evaluator = new OperationalHealthEvaluator([
            Check("Database", OperationalHealthStatus.Healthy, required: true),
            Check("Aggregator", OperationalHealthStatus.Degraded, required: false)
        ]);

        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Degraded, result.Status);
    }

    [UnitFact]
    public async Task GivenRequiredServiceDown_WhenEvaluated_ThenAggregateIsUnhealthy()
    {
        var evaluator = new OperationalHealthEvaluator([
            Check("Database", OperationalHealthStatus.Unhealthy, required: true),
            Check("Aggregator", OperationalHealthStatus.Degraded, required: false)
        ]);

        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Unhealthy, result.Status);
    }

    [UnitFact]
    public async Task GivenOptionalCheckThrows_WhenEvaluated_ThenItIsReportedAndOthersStillRun()
    {
        var evaluator = new OperationalHealthEvaluator([
            new ThrowingHealthCheck("Aggregator", required: false),
            Check("Database", OperationalHealthStatus.Healthy, required: true)
        ]);

        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Degraded, result.Status);
        Assert.Equal(OperationalHealthStatus.Unhealthy,
            result.Services.Single(service => service.Name == "Aggregator").Status);
        Assert.Equal(OperationalHealthStatus.Healthy,
            result.Services.Single(service => service.Name == "Database").Status);
    }

    [UnitFact]
    public async Task GivenRequiredCheckThrows_WhenEvaluated_ThenAggregateIsUnhealthy()
    {
        var evaluator = new OperationalHealthEvaluator([
            new ThrowingHealthCheck("Database", required: true)
        ]);

        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(OperationalHealthStatus.Unhealthy, result.Status);
        Assert.True(Assert.Single(result.Services).IsRequired);
    }

    [UnitFact]
    public async Task GivenCancelledEvaluation_WhenACheckObservesIt_ThenCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var evaluator = new OperationalHealthEvaluator([
            new ThrowingHealthCheck("Database", required: true, cancelled: true)
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(cancellation.Token));
    }

    private sealed class ThrowingHealthCheck(string name, bool required, bool cancelled = false)
        : IOperationalHealthCheck
    {
        public string Name => name;
        public bool IsRequired => required;

        public Task<OperationalHealthCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            cancelled
                ? Task.FromCanceled<OperationalHealthCheckResult>(cancellationToken)
                : throw new HttpRequestException("probe failed");
    }

    private static IOperationalHealthCheck Check(
        string name,
        OperationalHealthStatus status,
        bool required) => new StubHealthCheck(name, status, required);

    private sealed class StubHealthCheck(
        string name,
        OperationalHealthStatus status,
        bool required) : IOperationalHealthCheck
    {
        public string Name => name;
        public bool IsRequired => required;

        public Task<OperationalHealthCheckResult> CheckAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new OperationalHealthCheckResult(Name, status, IsRequired));
    }
}
