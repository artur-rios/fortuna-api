namespace ArturRios.Fortuna.Shared.Ingestion;

public enum DataSourceKind
{
    Network = 1,
    File = 2
}

public interface IDataSourceCatalog
{
    IReadOnlyCollection<DataSourceSnapshot> List();
}

public sealed record DataSourceSnapshot(
    string Name,
    DataSourceKind Kind,
    string DisplayName,
    bool IsNetworkBacked,
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyCollection<string> RequiredInputs,
    IReadOnlyCollection<string> SupportedFormats,
    IReadOnlyCollection<string> SupportedLayouts);
