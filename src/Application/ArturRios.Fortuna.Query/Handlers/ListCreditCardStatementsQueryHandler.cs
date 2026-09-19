using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCreditCardStatementsQueryHandler(
    ICurrentProfileResolver profileResolver,
    ICreditCardReader cards,
    ICreditCardStatementReader statements,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListCreditCardStatementsQuery, CreditCardStatementOutput>
{
    public async Task<PaginatedOutput<CreditCardStatementOutput>> HandleAsync(
        ListCreditCardStatementsQuery query)
    {
        var output = PaginatedOutput<CreditCardStatementOutput>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardStatementMessages.ProfileNotFound);
        }

        var card = await cards.FindByIdWithLimitsAsync(
            profile.Id,
            query.CreditCardId,
            CancellationToken.None);
        if (card is null)
        {
            return output.WithError(CreditCardStatementMessages.CreditCardNotFound);
        }

        var filtered = statements.Query(profile.Id)
            .Where(statement => statement.CreditCardId == card.Id);
        if (query.Status.HasValue)
        {
            var status = query.Status.Value;
            filtered = filtered.Where(statement => statement.Status == status);
        }

        // A statement matches when its period overlaps the requested range, so one that
        // straddles either bound is still listed.
        if (query.From.HasValue)
        {
            var from = query.From.Value;
            filtered = filtered.Where(statement => statement.PeriodEnd >= from);
        }

        if (query.To.HasValue)
        {
            var to = query.To.Value;
            filtered = filtered.Where(statement => statement.PeriodStart <= to);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(CreditCardStatementProjection.Expression);
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(CreditCardStatementMessages.ListedSuccessfully);
    }

    private static IOrderedQueryable<CreditCardStatementReadSnapshot> Order(
        IQueryable<CreditCardStatementReadSnapshot> statements,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "periodend" => statements
                .SortBy(statement => statement.PeriodEnd, statement => statement.Id, descending),
            "closingdate" => statements
                .SortBy(statement => statement.ClosingDate, statement => statement.Id, descending),
            "duedate" => statements
                .SortBy(statement => statement.DueDate, statement => statement.Id, descending),
            "status" => statements
                .SortBy(statement => statement.Status, statement => statement.Id, descending),
            "purchasetotal" => statements
                .SortBy(statement => statement.PurchaseTotal, statement => statement.Id, descending),
            "amountdue" => statements
                .SortBy(statement => statement.AmountDue, statement => statement.Id, descending),
            "createdat" => statements
                .SortBy(statement => statement.CreatedAt, statement => statement.Id, descending),
            "updatedat" => statements
                .SortBy(statement => statement.UpdatedAt, statement => statement.Id, descending),
            _ => statements
                .SortBy(statement => statement.PeriodStart, statement => statement.Id, descending)
        };
}
