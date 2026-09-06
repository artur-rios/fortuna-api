using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed record PluggySourceOptions(
    string? ClientId,
    string? ClientSecret,
    Uri? BaseUri,
    bool IsNetworkAvailable);

public sealed class PluggyIngestionSource(PluggySourceOptions options) : IIngestionSource
{
    public string Name => "pluggy";

    public DataSourceSnapshot Describe()
    {
        var configured = !string.IsNullOrWhiteSpace(options.ClientId) &&
            !string.IsNullOrWhiteSpace(options.ClientSecret) &&
            options.BaseUri is not null;
        var available = options.IsNetworkAvailable && configured;
        return new DataSourceSnapshot(
            Name,
            DataSourceKind.Network,
            "Pluggy open banking",
            true,
            available,
            available
                ? null
                : options.IsNetworkAvailable
                    ? DataSourceMessages.ConfigurationRequired
                    : DataSourceMessages.NetworkUnavailable,
            ["Configured Pluggy client", "Authorized institution connection"],
            [],
            []);
    }
}
