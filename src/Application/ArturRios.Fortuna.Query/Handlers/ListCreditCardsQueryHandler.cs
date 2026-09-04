using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCreditCardsQueryHandler(
    IValidator<ListCreditCardsQuery> validator,
    IUserProfileReader profiles,
    ICreditCardReader cards,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListCreditCardsQuery, CreditCardOutput>
{
    public async Task<PaginatedOutput<CreditCardOutput>> HandleAsync(ListCreditCardsQuery query)
    {
        var output = PaginatedOutput<CreditCardOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CreditCardMessages.ProfileNotFound);
        }

        var filtered = cards.QueryLimits().Where(card =>
            card.UserId == profile.Id && !card.IsDeleted);
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
        var projected = ordered.Select(card => new CreditCardOutput
        {
            Id = card.Id,
            Name = card.Name,
            Issuer = card.Issuer,
            CurrencyCode = card.CurrencyCode,
            CreditLimit = card.CreditLimit,
            UsedAmount = Math.Max(card.OutstandingAmount, 0m),
            AvailableAmount = Math.Max(
                card.CreditLimit - Math.Max(card.OutstandingAmount, 0m),
                0m),
            OverageAmount = Math.Max(
                Math.Max(card.OutstandingAmount, 0m) - card.CreditLimit,
                0m),
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            LastFourDigits = card.LastFourDigits,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt
        });
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(CreditCardMessages.ListedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IOrderedQueryable<CreditCardLimitSnapshot> Order(
        IQueryable<CreditCardLimitSnapshot> cards,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("issuer", false) => cards.OrderBy(card => card.Issuer).ThenBy(card => card.Id),
            ("issuer", true) => cards.OrderByDescending(card => card.Issuer)
                .ThenByDescending(card => card.Id),
            ("currencycode", false) => cards.OrderBy(card => card.CurrencyCode)
                .ThenBy(card => card.Id),
            ("currencycode", true) => cards.OrderByDescending(card => card.CurrencyCode)
                .ThenByDescending(card => card.Id),
            ("creditlimit", false) => cards.OrderBy(card => card.CreditLimit)
                .ThenBy(card => card.Id),
            ("creditlimit", true) => cards.OrderByDescending(card => card.CreditLimit)
                .ThenByDescending(card => card.Id),
            ("usedamount", false) => cards.OrderBy(card => card.OutstandingAmount)
                .ThenBy(card => card.Id),
            ("usedamount", true) => cards.OrderByDescending(card => card.OutstandingAmount)
                .ThenByDescending(card => card.Id),
            ("createdat", false) => cards.OrderBy(card => card.CreatedAt).ThenBy(card => card.Id),
            ("createdat", true) => cards.OrderByDescending(card => card.CreatedAt)
                .ThenByDescending(card => card.Id),
            ("updatedat", false) => cards.OrderBy(card => card.UpdatedAt).ThenBy(card => card.Id),
            ("updatedat", true) => cards.OrderByDescending(card => card.UpdatedAt)
                .ThenByDescending(card => card.Id),
            (_, false) => cards.OrderBy(card => card.Name).ThenBy(card => card.Id),
            _ => cards.OrderByDescending(card => card.Name).ThenByDescending(card => card.Id)
        };
}
