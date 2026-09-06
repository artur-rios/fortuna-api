using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class DataSourceListOutput : QueryOutput
{
    public IReadOnlyCollection<DataSourceOutput> Sources { get; set; } = [];
}

public sealed class DataSourceOutput
{
    public string Name { get; set; } = string.Empty;
    public DataSourceKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsNetworkBacked { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
    public IReadOnlyCollection<string> RequiredInputs { get; set; } = [];
    public IReadOnlyCollection<string> SupportedFormats { get; set; } = [];
    public IReadOnlyCollection<string> SupportedLayouts { get; set; } = [];
}
