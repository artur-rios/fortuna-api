using System.Text.Json.Serialization;
using ArturRios.Fortuna.Shared.Health;

namespace ArturRios.Fortuna.WebApi.Output;

public sealed class OperationalHealthOutput
{
    public string Status { get; init; } = string.Empty;
    public IReadOnlyCollection<OperationalHealthServiceOutput> Services { get; init; } = [];

    public static OperationalHealthOutput From(OperationalHealthReport report) => new()
    {
        Status = report.Status.ToString(),
        Services = report.Services.Select(service => new OperationalHealthServiceOutput
        {
            Name = service.Name,
            Status = service.Status.ToString(),
            QueueDepth = service.QueueDepth,
            OldestPendingSeconds = service.OldestPendingSeconds
        }).ToArray()
    };
}

public sealed class OperationalHealthServiceOutput
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? QueueDepth { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OldestPendingSeconds { get; init; }
}
