using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetNetPositionQueryHandler(
    ICurrentProfileResolver profileResolver,
    INetPositionReader positions,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetNetPositionQuery, NetPositionOutput>
{
    public async Task<DataOutput<NetPositionOutput?>> HandleAsync(GetNetPositionQuery query)
    {
        var output = DataOutput<NetPositionOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(NetPositionMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(
            displayCode,
            CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(NetPositionMessages.DisplayCurrencyUnsupported)
                .WithMessage(NetPositionMessages.UnknownCurrency(displayCode));
        }

        // Point-in-time position: every currency group converts at the as-of date.
        var asOf = query.AsOf ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var sourceGroups = await positions.ReadAsync(profile.Id, asOf, CancellationToken.None);
        var converter = new FigureConverter(rates, displayCurrency);
        var conversions = new List<FigureConversion>(sourceGroups.Count);
        var groups = new List<NetPositionCurrencyOutput>(sourceGroups.Count);
        foreach (var source in sourceGroups.OrderBy(item => item.CurrencyCode, StringComparer.Ordinal))
        {
            var conversion = await converter.ConvertAsync(source.CurrencyCode, source.Net, asOf);
            conversions.Add(conversion);
            groups.Add(new NetPositionCurrencyOutput
            {
                SourceCurrencyCode = source.CurrencyCode,
                FinancialAccounts = source.FinancialAccounts,
                Investments = source.Investments,
                CreditCards = source.CreditCards,
                SourceNet = source.Net,
                DisplayNet = converter.Round(conversion.Value),
                AppliedRate = conversion.Rate?.Rate,
                RateDate = conversion.Rate?.RateDate,
                RateSource = conversion.Rate?.Source,
                UnconvertedReason = conversion.UnconvertedReason
            });
        }

        var total = converter.Total(conversions);

        return output
            .WithData(new NetPositionOutput
            {
                AsOf = asOf,
                DisplayCurrencyCode = displayCurrency.Code,
                Total = total,
                IsFullyConverted = total.HasValue,
                CurrencyGroups = groups,
                MissingRates = MissingExchangeRateOutput.From(converter)
            })
            .WithMessage(total.HasValue
                ? NetPositionMessages.RetrievedSuccessfully
                : NetPositionMessages.PartiallyConverted);
    }
}
