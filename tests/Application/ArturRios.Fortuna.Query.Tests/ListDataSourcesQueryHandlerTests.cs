using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ListDataSourcesQueryHandlerTests
{
    [UnitFact]
    public async Task GivenRegisteredSources_WhenListed_ThenEveryDescriptorIsMapped()
    {
        var source = new DataSourceSnapshot(
            "excel",
            DataSourceKind.File,
            "Excel workbook",
            false,
            true,
            null,
            ["Workbook file"],
            [".xlsx"],
            ["Caller-mapped worksheet"]);
        var handler = new ListDataSourcesQueryHandler(new StubCatalog([source]));

        var result = await handler.HandleAsync(new ListDataSourcesQuery());

        Assert.True(result.Success);
        var output = Assert.Single(result.Data!.Sources);
        Assert.Equal(source.Name, output.Name);
        Assert.Equal(source.Kind, output.Kind);
        Assert.Equal(source.RequiredInputs, output.RequiredInputs);
        Assert.Equal(source.SupportedFormats, output.SupportedFormats);
        Assert.Equal(source.SupportedLayouts, output.SupportedLayouts);
        Assert.Contains(DataSourceMessages.ListedSuccessfully, result.Messages);
    }

    private sealed class StubCatalog(IReadOnlyCollection<DataSourceSnapshot> sources)
        : IDataSourceCatalog
    {
        public IReadOnlyCollection<DataSourceSnapshot> List() => sources;
    }
}
