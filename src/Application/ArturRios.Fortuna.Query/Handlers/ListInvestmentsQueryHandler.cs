using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListInvestmentsQueryHandler(
    ICurrentProfileResolver profileResolver,
    IInvestmentReader investments,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    PaginationOptions paginationOptions,
    TimeProvider timeProvider)
    : IPaginatedQueryHandlerAsync<ListInvestmentsQuery, InvestmentOutput>
{
    public async Task<PaginatedOutput<InvestmentOutput>> HandleAsync(ListInvestmentsQuery query)
    {
        var output = PaginatedOutput<InvestmentOutput>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(InvestmentMessages.CurrencyNotSupported)
                .WithMessage(InvestmentMessages.UnknownCurrency(displayCode));
        }

        var filtered = investments.QueryPositions().Where(investment =>
            investment.UserId == profile.Id);
        if (!query.IncludeDeleted)
        {
            filtered = filtered.Where(investment => !investment.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(query.Instrument))
        {
            var instrument = query.Instrument.Trim().ToLowerInvariant();
            filtered = filtered.Where(investment =>
                investment.Instrument.ToLower().Contains(instrument));
        }

        if (!string.IsNullOrWhiteSpace(query.Institution))
        {
            var institution = query.Institution.Trim().ToLowerInvariant();
            filtered = filtered.Where(investment =>
                investment.Institution != null &&
                investment.Institution.ToLower().Contains(institution));
        }

        if (query.InvestmentType.HasValue)
        {
            var investmentType = query.InvestmentType.Value;
            filtered = filtered.Where(investment =>
                investment.InvestmentType == investmentType);
        }

        if (!string.IsNullOrWhiteSpace(query.CurrencyCode))
        {
            var currencyCode = query.CurrencyCode.Trim().ToUpperInvariant();
            filtered = filtered.Where(investment =>
                investment.CurrencyCode == currencyCode);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(InvestmentPositionProjection.Expression);
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);
        var converter = new FigureConverter(rates, displayCurrency);
        var figureDate = query.FigureDate ?? Today();
        foreach (var investment in page.Data ?? [])
        {
            await InvestmentPositionProjection.ApplyConversionAsync(
                investment,
                converter,
                figureDate);
        }

        return page.WithMessage(InvestmentMessages.ListedSuccessfully);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static IOrderedQueryable<InvestmentPositionSnapshot> Order(
        IQueryable<InvestmentPositionSnapshot> investments,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "institution" => investments
                .SortBy(item => item.Institution, item => item.Id, descending),
            "investmenttype" => investments
                .SortBy(item => item.InvestmentType, item => item.Id, descending),
            "currencycode" => investments
                .SortBy(item => item.CurrencyCode, item => item.Id, descending),
            "position" => investments.SortBy(item => item.Position, item => item.Id, descending),
            "createdat" => investments.SortBy(item => item.CreatedAt, item => item.Id, descending),
            "updatedat" => investments.SortBy(item => item.UpdatedAt, item => item.Id, descending),
            _ => investments.SortBy(item => item.Instrument, item => item.Id, descending)
        };
}
