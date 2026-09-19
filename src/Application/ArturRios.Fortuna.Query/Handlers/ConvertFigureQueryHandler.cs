using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ConvertFigureQueryHandler(
    IValidator<ConvertFigureQuery> validator,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IUserProfileReader profiles)
    : IQueryHandlerAsync<ConvertFigureQuery, ConvertFigureQueryOutput>
{
    public async Task<DataOutput<ConvertFigureQueryOutput?>> HandleAsync(ConvertFigureQuery query)
    {
        var output = DataOutput<ConvertFigureQueryOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var displayCode = await ResolveDisplayCurrencyAsync(query);
        if (displayCode is null)
        {
            return output.WithError(FigureConversionMessages.ProfileNotFound);
        }

        var supported = (await currencies.ListAsync(CancellationToken.None))
            .ToDictionary(currency => currency.Code, StringComparer.Ordinal);
        if (!supported.TryGetValue(displayCode, out var displayCurrency))
        {
            return output
                .WithError(FigureConversionMessages.CurrencyNotSupported)
                .WithMessage(FigureConversionMessages.UnknownCurrency(displayCode));
        }

        var groups = (query.Amounts ?? [])
            .GroupBy(
                amount => amount.CurrencyCode.Trim().ToUpperInvariant(),
                StringComparer.Ordinal)
            .Select(group => new SourceCurrencyGroup(group.Key, group.Sum(item => item.Amount)))
            .OrderBy(group => group.CurrencyCode, StringComparer.Ordinal)
            .ToArray();
        var unsupported = groups.FirstOrDefault(group => !supported.ContainsKey(group.CurrencyCode));
        if (unsupported is not null)
        {
            return output
                .WithError(FigureConversionMessages.CurrencyNotSupported)
                .WithMessage(FigureConversionMessages.UnknownCurrency(unsupported.CurrencyCode));
        }

        // Point-in-time figure: every group converts at the requested figure date.
        var converter = new FigureConverter(rates, displayCurrency);
        var conversions = new List<FigureConversion>(groups.Length);
        foreach (var group in groups)
        {
            conversions.Add(await converter.ConvertAsync(
                group.CurrencyCode,
                group.Amount,
                query.FigureDate));
        }

        var total = converter.Total(conversions);

        return output
            .WithData(new ConvertFigureQueryOutput
            {
                DisplayCurrencyCode = displayCode,
                FigureDate = query.FigureDate,
                Total = total,
                IsFullyConverted = total.HasValue,
                Groups = conversions.Select(conversion => new ConvertedCurrencyGroupOutput
                {
                    SourceCurrencyCode = conversion.SourceCurrencyCode,
                    SourceAmount = conversion.SourceAmount,
                    DisplayAmount = converter.Round(conversion.Value),
                    AppliedRate = conversion.Rate?.Rate,
                    RateDate = conversion.Rate?.RateDate,
                    RateSource = conversion.Rate?.Source,
                    UnconvertedReason = conversion.UnconvertedReason
                }).ToArray(),
                MissingRates = MissingExchangeRateOutput.From(converter)
            })
            .WithMessage(total.HasValue
                ? FigureConversionMessages.ConvertedSuccessfully
                : FigureConversionMessages.PartiallyConverted);
    }

    private async Task<string?> ResolveDisplayCurrencyAsync(ConvertFigureQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.DisplayCurrencyCode))
        {
            return query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        }

        var profile = query.IsLocal
            ? await profiles.FindByPublicIdAsync(query.ExternalSubject, CancellationToken.None)
            : await profiles.FindByExternalSubjectAsync(query.ExternalSubject, CancellationToken.None);

        return profile?.DisplayCurrency.ToUpperInvariant();
    }

    private sealed record SourceCurrencyGroup(string CurrencyCode, decimal Amount);
}
