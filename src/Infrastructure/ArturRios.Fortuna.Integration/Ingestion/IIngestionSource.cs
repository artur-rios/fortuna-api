using ArturRios.Fortuna.Shared.Ingestion;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed record IngestionPayload(string Source, IReadOnlyList<ReadOnlyMemory<byte>> Items);

public interface IIngestionSource
{
    string Name { get; }
    DataSourceSnapshot Describe();
}

public interface IFileIngestionSource : IIngestionSource
{
    Task<IngestionPayload> ReadAsync(Stream content, CancellationToken cancellationToken);
}
