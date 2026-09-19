using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCreditCardsQueryHandler(
    ICurrentProfileResolver profileResolver,
    ICreditCardReader cards,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListCreditCardsQuery, CreditCardOutput>
{
    public async Task<PaginatedOutput<CreditCardOutput>> HandleAsync(ListCreditCardsQuery query)
    {
        var output = PaginatedOutput<CreditCardOutput>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardMessages.ProfileNotFound);
        }

        var filtered = cards.QueryLimits().Where(card => card.UserId == profile.Id);
        if (!query.IncludeDeleted)
        {
            filtered = filtered.Where(card => !card.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim().ToLowerInvariant();
            filtered = filtered.Where(card => card.Name.ToLower().Contains(name));
        }

        if (!string.IsNullOrWhiteSpace(query.Issuer))
        {
            var issuer = query.Issuer.Trim().ToLowerInvariant();
            filtered = filtered.Where(card => card.Issuer.ToLower().Contains(issuer));
        }

        if (!string.IsNullOrWhiteSpace(query.CurrencyCode))
        {
            var currencyCode = query.CurrencyCode.Trim().ToUpperInvariant();
            filtered = filtered.Where(card => card.CurrencyCode == currencyCode);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(CreditCardProjection.Expression);
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(CreditCardMessages.ListedSuccessfully);
    }

    private static IOrderedQueryable<CreditCardLimitSnapshot> Order(
        IQueryable<CreditCardLimitSnapshot> cards,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "issuer" => cards.SortBy(card => card.Issuer, card => card.Id, descending),
            "currencycode" => cards.SortBy(card => card.CurrencyCode, card => card.Id, descending),
            "creditlimit" => cards.SortBy(card => card.CreditLimit, card => card.Id, descending),
            "usedamount" => cards
                .SortBy(card => card.OutstandingAmount, card => card.Id, descending),
            "createdat" => cards.SortBy(card => card.CreatedAt, card => card.Id, descending),
            "updatedat" => cards.SortBy(card => card.UpdatedAt, card => card.Id, descending),
            _ => cards.SortBy(card => card.Name, card => card.Id, descending)
        };
}
