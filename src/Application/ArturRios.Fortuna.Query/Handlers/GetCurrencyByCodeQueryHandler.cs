using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCurrencyByCodeQueryHandler(ICurrencyReader currencies)
    : IQueryHandlerAsync<GetCurrencyByCodeQuery, CurrencyOutput>
{
    public async Task<DataOutput<CurrencyOutput?>> HandleAsync(GetCurrencyByCodeQuery query)
    {
        var currency = await currencies.FindByCodeAsync(
            query.Code.ToUpperInvariant(),
            CancellationToken.None);
        if (currency is null)
        {
            return DataOutput<CurrencyOutput?>.New.WithError(CurrencyMessages.CurrencyNotFound);
        }

        return DataOutput<CurrencyOutput?>.New
            .WithData(CurrencyProjection.From(currency))
            .WithMessage(CurrencyMessages.CurrencyRetrievedSuccessfully);
    }
}
