using ArturRios.Fortuna.Integration.Ingestion;
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

    private sealed class StubSource(string name) : IIngestionSource
    {
        public string Name { get; } = name;
        public bool IsAvailable => true;
        public Task<IngestionPayload> ReadAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(new IngestionPayload(Name, []));
    }
}
