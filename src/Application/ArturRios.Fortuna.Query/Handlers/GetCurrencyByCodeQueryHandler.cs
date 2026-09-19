using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCurrencyByCodeQueryHandler(
    IValidator<GetCurrencyByCodeQuery> validator,
    ICurrencyReader currencies)
    : IQueryHandlerAsync<GetCurrencyByCodeQuery, CurrencyOutput>
{
    public async Task<DataOutput<CurrencyOutput?>> HandleAsync(GetCurrencyByCodeQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<CurrencyOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var currency = await currencies.FindByCodeAsync(
            query.Code.Trim().ToUpperInvariant(),
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
