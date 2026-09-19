using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListFinancialAccountsQueryHandler(
    ICurrentProfileResolver profileResolver,
    IFinancialAccountReader accounts,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListFinancialAccountsQuery, FinancialAccountOutput>
{
    public async Task<PaginatedOutput<FinancialAccountOutput>> HandleAsync(
        ListFinancialAccountsQuery query)
    {
        var output = PaginatedOutput<FinancialAccountOutput>.New;
        var profile = await profileResolver.ResolveAsync();
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

    private static IOrderedQueryable<FinancialAccount> Order(
        IQueryable<FinancialAccount> accounts,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "institution" => accounts
                .SortBy(account => account.Institution, account => account.PublicId, descending),
            "accounttype" => accounts
                .SortBy(account => account.AccountType, account => account.PublicId, descending),
            "currencycode" => accounts
                .SortBy(account => account.Currency.Code, account => account.PublicId, descending),
            "openingbalance" => accounts
                .SortBy(account => account.OpeningBalance, account => account.PublicId, descending),
            "createdat" => accounts
                .SortBy(account => account.CreatedAt, account => account.PublicId, descending),
            "updatedat" => accounts
                .SortBy(account => account.UpdatedAt, account => account.PublicId, descending),
            _ => accounts.SortBy(account => account.Name, account => account.PublicId, descending)
        };
}
