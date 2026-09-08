using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Shared.Health;

namespace ArturRios.Fortuna.Data.Health;

public sealed class DatabaseOperationalHealthCheck(AppDbContext context)
    : IOperationalHealthCheck
{
    public string Name => "Database";
    public bool IsRequired => true;

    public async Task<OperationalHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var healthy = await context.Database.CanConnectAsync(cancellationToken);
            return Result(healthy
                ? OperationalHealthStatus.Healthy
                : OperationalHealthStatus.Unhealthy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result(OperationalHealthStatus.Unhealthy);
        }
    }

    private OperationalHealthCheckResult Result(OperationalHealthStatus status) =>
        new(Name, status, IsRequired);
}
