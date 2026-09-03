namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed class FileUploadIngestionSource : IIngestionSource
{
    public string Name => "file-upload";
    public bool IsAvailable => true;

    public async Task<IngestionPayload> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return new IngestionPayload(Name, new[] { new ReadOnlyMemory<byte>(buffer.ToArray()) });
    }
}
