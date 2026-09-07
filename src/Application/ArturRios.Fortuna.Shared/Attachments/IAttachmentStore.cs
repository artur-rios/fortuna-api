namespace ArturRios.Fortuna.Shared.Attachments;

public interface IAttachmentStore
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

public sealed class AttachmentObjectNotFoundException(string key)
    : IOException($"The attachment object '{key}' was not found.");
