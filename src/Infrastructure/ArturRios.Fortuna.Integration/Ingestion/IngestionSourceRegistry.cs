using ArturRios.Fortuna.Shared.Ingestion;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed class IngestionSourceRegistry : IDataSourceCatalog
{
    private readonly IReadOnlyDictionary<string, IIngestionSource> sources;

    public IngestionSourceRegistry(IEnumerable<IIngestionSource> sources)
    {
        var registered = new Dictionary<string, IIngestionSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (!registered.TryAdd(source.Name, source))
            {
                throw new InvalidOperationException($"An ingestion source named '{source.Name}' is already registered.");
            }
        }

        this.sources = registered;
    }

    public IReadOnlyCollection<IIngestionSource> All => sources.Values.ToArray();

    public IReadOnlyCollection<DataSourceSnapshot> List() => sources.Values
        .Select(source => source.Describe())
        .OrderBy(source => source.DisplayName)
        .ThenBy(source => source.Name)
        .ToArray();

    public IIngestionSource Get(string name) => sources.TryGetValue(name, out var source)
        ? source
        : throw new KeyNotFoundException($"No ingestion source named '{name}' is registered.");
}
