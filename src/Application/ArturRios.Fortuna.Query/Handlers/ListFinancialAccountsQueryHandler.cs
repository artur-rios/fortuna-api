using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListFinancialAccountsQueryHandler(
    IValidator<ListFinancialAccountsQuery> validator,
    IUserProfileReader profiles,
    IFinancialAccountReader accounts,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListFinancialAccountsQuery, FinancialAccountOutput>
{
    public async Task<PaginatedOutput<FinancialAccountOutput>> HandleAsync(
        ListFinancialAccountsQuery query)
    {
        var output = PaginatedOutput<FinancialAccountOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var filtered = accounts.Query().Where(account => account.User.PublicId == profile.Id);
        if (!query.IncludeDeleted)
        {
            filtered = filtered.Where(account => !account.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim().ToLowerInvariant();
            filtered = filtered.Where(account => account.Name.ToLower().Contains(name));
        }

        if (!string.IsNullOrWhiteSpace(query.Institution))
        {
            var institution = query.Institution.Trim().ToLowerInvariant();
            filtered = filtered.Where(account =>
                account.Institution != null && account.Institution.ToLower().Contains(institution));
        }

        if (query.AccountType.HasValue)
        {
            var accountType = query.AccountType.Value;
            filtered = filtered.Where(account => account.AccountType == accountType);
        }

        if (!string.IsNullOrWhiteSpace(query.CurrencyCode))
        {
            var currencyCode = query.CurrencyCode.Trim().ToUpperInvariant();
            filtered = filtered.Where(account => account.Currency.Code == currencyCode);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(account => new FinancialAccountOutput
        {
            Id = account.PublicId,
            Name = account.Name,
            Institution = account.Institution,
            AccountType = account.AccountType,
            CurrencyCode = account.Currency.Code,
            OpeningBalance = account.OpeningBalance,
            IsDeleted = account.IsDeleted,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        });
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(FinancialAccountMessages.ListedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IOrderedQueryable<FinancialAccount> Order(
        IQueryable<FinancialAccount> accounts,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("institution", false) => accounts.OrderBy(account => account.Institution)
                .ThenBy(account => account.PublicId),
            ("institution", true) => accounts.OrderByDescending(account => account.Institution)
                .ThenByDescending(account => account.PublicId),
            ("accounttype", false) => accounts.OrderBy(account => account.AccountType)
                .ThenBy(account => account.PublicId),
            ("accounttype", true) => accounts.OrderByDescending(account => account.AccountType)
                .ThenByDescending(account => account.PublicId),
            ("currencycode", false) => accounts.OrderBy(account => account.Currency.Code)
                .ThenBy(account => account.PublicId),
            ("currencycode", true) => accounts.OrderByDescending(account => account.Currency.Code)
                .ThenByDescending(account => account.PublicId),
            ("openingbalance", false) => accounts.OrderBy(account => account.OpeningBalance)
                .ThenBy(account => account.PublicId),
            ("openingbalance", true) => accounts.OrderByDescending(account => account.OpeningBalance)
                .ThenByDescending(account => account.PublicId),
            ("createdat", false) => accounts.OrderBy(account => account.CreatedAt)
                .ThenBy(account => account.PublicId),
            ("createdat", true) => accounts.OrderByDescending(account => account.CreatedAt)
                .ThenByDescending(account => account.PublicId),
            ("updatedat", false) => accounts.OrderBy(account => account.UpdatedAt)
                .ThenBy(account => account.PublicId),
            ("updatedat", true) => accounts.OrderByDescending(account => account.UpdatedAt)
                .ThenByDescending(account => account.PublicId),
            (_, false) => accounts.OrderBy(account => account.Name)
                .ThenBy(account => account.PublicId),
            _ => accounts.OrderByDescending(account => account.Name)
                .ThenByDescending(account => account.PublicId)
        };
}
