using System.Diagnostics.CodeAnalysis;

namespace ArturRios.Fortuna.Shared.Attachments;

public interface IAttachmentStore
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stored object. A missing object or an unreachable store is reported through the
    /// returned status instead of an exception; the caller owns and disposes the content stream.
    /// </summary>
    Task<AttachmentReadResult> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes a stored object. Deleting an object that does not exist succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

public enum AttachmentReadStatus
{
    Found = 1,
    NotFound = 2,
    Unavailable = 3
}

public sealed record AttachmentReadResult(AttachmentReadStatus Status, Stream? Content)
{
    [MemberNotNullWhen(true, nameof(Content))]
    public bool IsFound => Status == AttachmentReadStatus.Found;

    public static AttachmentReadResult NotFound { get; } = new(AttachmentReadStatus.NotFound, null);

    public static AttachmentReadResult Unavailable { get; } = new(AttachmentReadStatus.Unavailable, null);

    public static AttachmentReadResult Found(Stream content) =>
        new(AttachmentReadStatus.Found, content ?? throw new ArgumentNullException(nameof(content)));
}
