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

        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
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
        var converted = new List<ConvertedCurrencyGroupOutput>(groups.Length);

        foreach (var group in groups)
        {
            if (await currencies.FindByCodeAsync(group.CurrencyCode, CancellationToken.None) is null)
            {
                return output
                    .WithError(FigureConversionMessages.CurrencyNotSupported)
                    .WithMessage(FigureConversionMessages.UnknownCurrency(group.CurrencyCode));
            }

            if (group.CurrencyCode == displayCode)
            {
                converted.Add(new ConvertedCurrencyGroupOutput
                {
                    SourceCurrencyCode = group.CurrencyCode,
                    SourceAmount = group.Amount,
                    DisplayAmount = Round(group.Amount, displayCurrency.MinorUnitDigits)
                });
                continue;
            }

            var rate = await rates.FindApplicableAsync(
                group.CurrencyCode,
                displayCode,
                query.FigureDate,
                CancellationToken.None);
            if (rate is null)
            {
                converted.Add(new ConvertedCurrencyGroupOutput
                {
                    SourceCurrencyCode = group.CurrencyCode,
                    SourceAmount = group.Amount,
                    UnconvertedReason = FigureConversionMessages.RateUnavailable
                });
                continue;
            }

            converted.Add(new ConvertedCurrencyGroupOutput
            {
                SourceCurrencyCode = group.CurrencyCode,
                SourceAmount = group.Amount,
                DisplayAmount = Round(group.Amount * rate.Rate, displayCurrency.MinorUnitDigits),
                AppliedRate = rate.Rate,
                RateDate = rate.RateDate,
                RateSource = rate.Source
            });
        }

        var fullyConverted = converted.All(group => group.DisplayAmount.HasValue);
        return output
            .WithData(new ConvertFigureQueryOutput
            {
                DisplayCurrencyCode = displayCode,
                FigureDate = query.FigureDate,
                Total = fullyConverted ? converted.Sum(group => group.DisplayAmount!.Value) : null,
                IsFullyConverted = fullyConverted,
                Groups = converted
            })
            .WithMessage(fullyConverted
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

    private static decimal Round(decimal amount, short digits) =>
        decimal.Round(amount, digits, MidpointRounding.AwayFromZero);

    private sealed record SourceCurrencyGroup(string CurrencyCode, decimal Amount);
}
