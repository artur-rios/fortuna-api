using System.Linq.Expressions;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class InvestmentPositionProjection
{
    public static readonly Expression<Func<InvestmentPositionSnapshot, InvestmentOutput>>
        Expression = investment => new InvestmentOutput
        {
            Id = investment.Id,
            Instrument = investment.Instrument,
            Institution = investment.Institution,
            InvestmentType = investment.InvestmentType,
            CurrencyCode = investment.CurrencyCode,
            Position = investment.Position,
            IsIndependentlyValued = investment.IsIndependentlyValued,
            LatestValuationValue = investment.LatestValuationValue,
            LatestValuationDate = investment.LatestValuationDate,
            CreatedAt = investment.CreatedAt,
            UpdatedAt = investment.UpdatedAt
        };

    public static InvestmentOutput Project(InvestmentPositionSnapshot investment) => new()
    {
        Id = investment.Id,
        Instrument = investment.Instrument,
        Institution = investment.Institution,
        InvestmentType = investment.InvestmentType,
        CurrencyCode = investment.CurrencyCode,
        Position = investment.Position,
        IsIndependentlyValued = investment.IsIndependentlyValued,
        LatestValuationValue = investment.LatestValuationValue,
        LatestValuationDate = investment.LatestValuationDate,
        CreatedAt = investment.CreatedAt,
        UpdatedAt = investment.UpdatedAt
    };

    public static async Task ApplyConversionAsync(
        InvestmentOutput investment,
        CurrencySnapshot? displayCurrency,
        DateOnly figureDate,
        IExchangeRateReader rates)
    {
        if (displayCurrency is null)
        {
            return;
        }

        investment.DisplayCurrencyCode = displayCurrency.Code;
        if (investment.CurrencyCode == displayCurrency.Code)
        {
            investment.DisplayPosition = Round(
                investment.Position,
                displayCurrency.MinorUnitDigits);
            return;
        }

        var rate = await rates.FindApplicableAsync(
            investment.CurrencyCode,
            displayCurrency.Code,
            figureDate,
            CancellationToken.None);
        if (rate is null)
        {
            investment.UnconvertedReason = FigureConversionMessages.RateUnavailable;
            return;
        }

        investment.DisplayPosition = Round(
            investment.Position * rate.Rate,
            displayCurrency.MinorUnitDigits);
        investment.AppliedRate = rate.Rate;
        investment.RateDate = rate.RateDate;
        investment.RateSource = rate.Source;
    }

    private static decimal Round(decimal amount, short digits) =>
        decimal.Round(amount, digits, MidpointRounding.AwayFromZero);
}
