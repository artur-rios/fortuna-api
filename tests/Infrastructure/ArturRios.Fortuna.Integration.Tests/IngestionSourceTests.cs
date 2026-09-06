using System.Text;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class IngestionSourceTests
{
    [UnitFact]
    public async Task GivenUploadedContent_WhenRead_ThenSinglePayloadPreservesEveryByte()
    {
        var source = new ExcelWorkbookIngestionSource();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("statement"));

        var payload = await source.ReadAsync(input, CancellationToken.None);

        var description = source.Describe();
        Assert.Equal("excel", source.Name);
        Assert.True(description.IsAvailable);
        Assert.Equal(DataSourceKind.File, description.Kind);
        Assert.Contains(".xlsx", description.SupportedFormats);
        Assert.Equal("excel", payload.Source);
        Assert.Single(payload.Items);
        Assert.Equal("statement", Encoding.UTF8.GetString(payload.Items[0].Span));
    }


    [UnitFact]
    public void GivenPluggyConfiguration_WhenDescribed_ThenAvailabilityExplainsDeploymentState()
    {
        var configured = new PluggyIngestionSource(new PluggySourceOptions(
            "client", "secret", new Uri("https://pluggy.example"), true));
        var missing = new PluggyIngestionSource(new PluggySourceOptions(
            null, null, null, true));
        var offline = new PluggyIngestionSource(new PluggySourceOptions(
            "client", "secret", new Uri("https://pluggy.example"), false));

        Assert.True(configured.Describe().IsAvailable);
        Assert.Equal(DataSourceMessages.ConfigurationRequired,
            missing.Describe().UnavailableReason);
        Assert.Equal(DataSourceMessages.NetworkUnavailable,
            offline.Describe().UnavailableReason);
    }

    [UnitFact]
    public void GivenNubankSource_WhenDescribed_ThenPdfLayoutIsAdvertised()
    {
        var description = new NubankInvoiceIngestionSource().Describe();

        Assert.Equal(DataSourceKind.File, description.Kind);
        Assert.False(description.IsNetworkBacked);
        Assert.Contains(".pdf", description.SupportedFormats);
        Assert.Contains("Nubank credit card invoice", description.SupportedLayouts);
    }
}
