namespace ArturRios.Fortuna.Integration.Storage;

public interface IAttachmentStore
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
