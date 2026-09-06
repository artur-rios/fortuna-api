using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class IngestionSourceRegistryTests
{
    [UnitFact]
    public void GivenRegisteredSource_WhenResolvedByName_ThenSourceIsReturned()
    {
        var source = new StubSource("file-upload");
        var registry = new IngestionSourceRegistry([source]);

        var resolved = registry.Get("file-upload");

        Assert.Same(source, resolved);
    }

    [UnitFact]
    public void GivenDuplicateNames_WhenRegistryIsBuilt_ThenConfigurationIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new IngestionSourceRegistry([new StubSource("duplicate"), new StubSource("DUPLICATE")]));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [UnitFact]
    public void GivenNewRegisteredSource_WhenListed_ThenItAppearsWithoutCatalogChanges()
    {
        var registry = new IngestionSourceRegistry([
            new StubSource("later"),
            new StubSource("earlier")]);

        var sources = registry.List();

        Assert.Equal(["earlier", "later"], sources.Select(item => item.Name));
    }

    private sealed class StubSource(string name) : IIngestionSource
    {
        public string Name { get; } = name;
        public DataSourceSnapshot Describe() => new(
            Name,
            DataSourceKind.File,
            Name,
            false,
            true,
            null,
            [],
            [],
            []);
    }
}
