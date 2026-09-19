namespace ArturRios.Fortuna.WebApi.Requests;

/// <summary>An uploaded form file read into memory once, or the reason it was not read.</summary>
public sealed record UploadedFile(string FileName, string ContentType, byte[] Content, bool TooLarge)
{
    /// <summary>
    ///     Checks the declared length against the limit before reading anything, then copies the
    ///     file straight into one exactly-sized buffer. A missing file yields empty content, which
    ///     the command validator reports as required.
    /// </summary>
    public static async Task<UploadedFile> ReadAsync(
        IFormFile? file,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return new UploadedFile(string.Empty, string.Empty, [], false);
        }

        if (file.Length > maximumBytes)
        {
            return new UploadedFile(file.FileName, string.Empty, [], true);
        }

        var content = new byte[file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(content, cancellationToken);

        return new UploadedFile(file.FileName, file.ContentType ?? string.Empty, content, false);
    }
}
