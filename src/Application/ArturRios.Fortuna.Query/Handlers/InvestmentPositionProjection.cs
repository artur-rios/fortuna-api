using System.Linq.Expressions;
using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Investments;

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
            IsDeleted = investment.IsDeleted,
            CreatedAt = investment.CreatedAt,
            UpdatedAt = investment.UpdatedAt
        };

    private static readonly Func<InvestmentPositionSnapshot, InvestmentOutput> Compiled =
        Expression.Compile();

    public static InvestmentOutput Project(InvestmentPositionSnapshot investment) =>
        Compiled(investment);

    /// <summary>
    /// Converts a point-in-time position, so the rate is looked up for the figure date.
    /// </summary>
    public static async Task ApplyConversionAsync(
        InvestmentOutput investment,
        FigureConverter converter,
        DateOnly figureDate)
    {
        var conversion = await converter.ConvertAsync(
            investment.CurrencyCode,
            investment.Position,
            figureDate);
        investment.DisplayCurrencyCode = converter.DisplayCurrency.Code;
        investment.DisplayPosition = converter.Round(conversion.Value);
        investment.AppliedRate = conversion.Rate?.Rate;
        investment.RateDate = conversion.Rate?.RateDate;
        investment.RateSource = conversion.Rate?.Source;
        investment.UnconvertedReason = conversion.UnconvertedReason;
    }
}
