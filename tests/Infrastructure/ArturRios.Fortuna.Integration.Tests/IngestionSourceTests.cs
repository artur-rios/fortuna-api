using System.Text;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class IngestionSourceTests
{
    [UnitFact]
    public async Task GivenUploadedContent_WhenRead_ThenSinglePayloadPreservesEveryByte()
    {
        var source = new FileUploadIngestionSource();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("statement"));

        var payload = await source.ReadAsync(input, CancellationToken.None);

        Assert.Equal("file-upload", source.Name);
        Assert.True(source.IsAvailable);
        Assert.Equal("file-upload", payload.Source);
        Assert.Single(payload.Items);
        Assert.Equal("statement", Encoding.UTF8.GetString(payload.Items[0].Span));
    }
}
