namespace ArturRios.Fortuna.Command.Input.Validation;

/// <summary>
///     Leading magic bytes of the file formats Fortuna reads, so an upload is judged by what it
///     contains rather than by the name or content type the client declared.
/// </summary>
public static class FileSignatures
{
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly Dictionary<string, byte[]> ByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = Pdf,
        ["image/jpeg"] = Jpeg,
        ["image/jpg"] = Jpeg,
        ["image/png"] = Png
    };

    public static bool IsPdf(byte[]? content) => StartsWith(content, Pdf);

    /// <summary>An .xlsx workbook is an Office Open XML package, which is a ZIP archive.</summary>
    public static bool IsZipPackage(byte[]? content) => StartsWith(content, Zip);

    /// <summary>
    ///     True when the content starts with the signature of the declared type. Types without a
    ///     known signature are not judged here; the allowed-type list still applies to them.
    /// </summary>
    public static bool MatchesContentType(string? contentType, byte[]? content) =>
        contentType is null ||
        !ByContentType.TryGetValue(contentType.Trim(), out var signature) ||
        StartsWith(content, signature);

    private static bool StartsWith(byte[]? content, byte[] signature) =>
        content is not null && content.AsSpan().StartsWith(signature);
}
