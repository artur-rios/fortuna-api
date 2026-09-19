using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListInvestmentValuationsQueryHandler(
    ICurrentProfileResolver profileResolver,
    IInvestmentReader investments,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListInvestmentValuationsQuery, InvestmentValuationOutput>
{
    public async Task<PaginatedOutput<InvestmentValuationOutput>> HandleAsync(
        ListInvestmentValuationsQuery query)
    {
        var output = PaginatedOutput<InvestmentValuationOutput>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        if (!await investments.ExistsAsync(
            profile.Id,
            query.InvestmentId,
            CancellationToken.None))
        {
            return output.WithError(InvestmentMessages.NotFound);
        }

        var filtered = investments.QueryValuations(profile.Id, query.InvestmentId);
        if (query.From.HasValue)
        {
            var from = query.From.Value;
            filtered = filtered.Where(valuation => valuation.ValuedOn >= from);
        }

        if (query.To.HasValue)
        {
            var to = query.To.Value;
            filtered = filtered.Where(valuation => valuation.ValuedOn <= to);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(valuation => new InvestmentValuationOutput
        {
            Id = valuation.Id,
            InvestmentId = valuation.InvestmentId,
            Value = valuation.Value,
            CurrencyCode = valuation.CurrencyCode,
            ValuedOn = valuation.ValuedOn,
            CreatedAt = valuation.CreatedAt,
            UpdatedAt = valuation.UpdatedAt
        });
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(InvestmentMessages.ValuationHistoryRetrievedSuccessfully);
    }

    private static IOrderedQueryable<InvestmentValuationReadSnapshot> Order(
        IQueryable<InvestmentValuationReadSnapshot> valuations,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "value" => valuations.SortBy(item => item.Value, item => item.Id, descending),
            "createdat" => valuations.SortBy(item => item.CreatedAt, item => item.Id, descending),
            "updatedat" => valuations.SortBy(item => item.UpdatedAt, item => item.Id, descending),
            _ => valuations.SortBy(item => item.ValuedOn, item => item.Id, descending)
        };
}
