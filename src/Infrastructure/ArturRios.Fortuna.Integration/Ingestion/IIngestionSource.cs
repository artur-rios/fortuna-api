namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed record IngestionPayload(string Source, IReadOnlyList<ReadOnlyMemory<byte>> Items);

public interface IIngestionSource
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<IngestionPayload> ReadAsync(Stream content, CancellationToken cancellationToken);
}
