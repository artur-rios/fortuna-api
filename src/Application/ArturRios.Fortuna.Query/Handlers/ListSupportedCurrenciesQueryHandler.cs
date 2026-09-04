using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListSupportedCurrenciesQueryHandler(ICurrencyReader currencies)
    : IQueryHandlerAsync<ListSupportedCurrenciesQuery, ListSupportedCurrenciesQueryOutput>
{
    public async Task<DataOutput<ListSupportedCurrenciesQueryOutput?>> HandleAsync(
        ListSupportedCurrenciesQuery query)
    {
        var references = await currencies.ListAsync(CancellationToken.None);

        return DataOutput<ListSupportedCurrenciesQueryOutput?>.New
            .WithData(new ListSupportedCurrenciesQueryOutput
            {
                Currencies = references.Select(CurrencyProjection.From).ToArray()
            })
            .WithMessage(CurrencyMessages.CurrenciesRetrievedSuccessfully);
    }
}
