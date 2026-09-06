using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListDataSourcesQueryHandler(IDataSourceCatalog sources)
    : IQueryHandlerAsync<ListDataSourcesQuery, DataSourceListOutput>
{
    public Task<DataOutput<DataSourceListOutput?>> HandleAsync(ListDataSourcesQuery query) =>
        Task.FromResult(DataOutput<DataSourceListOutput?>.New.WithData(new DataSourceListOutput
        {
            Sources = sources.List().Select(source => new DataSourceOutput
            {
                Name = source.Name,
                Kind = source.Kind,
                DisplayName = source.DisplayName,
                IsNetworkBacked = source.IsNetworkBacked,
                IsAvailable = source.IsAvailable,
                UnavailableReason = source.UnavailableReason,
                RequiredInputs = source.RequiredInputs,
                SupportedFormats = source.SupportedFormats,
                SupportedLayouts = source.SupportedLayouts
            }).ToArray()
        }).WithMessage(DataSourceMessages.ListedSuccessfully));
}
