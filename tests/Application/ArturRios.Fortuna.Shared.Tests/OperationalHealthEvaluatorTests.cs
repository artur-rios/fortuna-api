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
