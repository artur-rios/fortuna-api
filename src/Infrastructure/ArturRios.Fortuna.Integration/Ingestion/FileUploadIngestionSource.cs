using ArturRios.Fortuna.Shared.Ingestion;

namespace ArturRios.Fortuna.Integration.Ingestion;

public abstract class FileUploadIngestionSource : IFileIngestionSource
{
    public abstract string Name { get; }
    public abstract DataSourceSnapshot Describe();

    public async Task<IngestionPayload> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return new IngestionPayload(Name, new[] { new ReadOnlyMemory<byte>(buffer.ToArray()) });
    }
}

public sealed class ExcelWorkbookIngestionSource : FileUploadIngestionSource
{
    public override string Name => "excel";

    public override DataSourceSnapshot Describe() => new(
        Name,
        DataSourceKind.File,
        "Excel workbook",
        false,
        true,
        null,
        ["Workbook file", "Column mapping"],
        [".xlsx"],
        ["Caller-mapped worksheet"]);
}

public sealed class NubankInvoiceIngestionSource : FileUploadIngestionSource
{
    public override string Name => "nubank-pdf";

    public override DataSourceSnapshot Describe() => new(
        Name,
        DataSourceKind.File,
        "Nubank credit card invoice",
        false,
        true,
        null,
        ["Invoice file"],
        [".pdf"],
        ["Nubank credit card invoice"]);
}
