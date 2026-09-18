using ArturRios.Fortuna.Shared.Attachments;

namespace ArturRios.Fortuna.Integration.Storage;

public sealed class FilesystemAttachmentStore : IAttachmentStore
{
    private const int BufferSize = 81920;
    private const string TemporarySuffix = ".tmp";

    private readonly string root;

    public FilesystemAttachmentStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A storage path is required.", nameof(root));
        }

        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{TemporarySuffix}");
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<AttachmentReadResult> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(AttachmentReadResult.NotFound);
        }

        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                useAsync: true);

            return Task.FromResult(AttachmentReadResult.Found(stream));
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // The object was removed between the existence check and the open.
            return Task.FromResult(AttachmentReadResult.NotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(AttachmentReadResult.Unavailable);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(key);
        if (Directory.Exists(Path.GetDirectoryName(path)))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(root))
        {
            return false;
        }

        var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}{TemporarySuffix}");
        try
        {
            await File.WriteAllBytesAsync(probe, [1], cancellationToken);
            File.Delete(probe);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
        {
            throw new ArgumentException("An attachment key must be a relative path.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("An attachment key cannot leave the configured storage root.", nameof(key));
        }

        return path;
    }
}
