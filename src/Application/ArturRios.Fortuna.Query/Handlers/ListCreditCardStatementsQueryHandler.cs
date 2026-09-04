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

public sealed class ListCreditCardStatementsQueryHandler(
    IValidator<ListCreditCardStatementsQuery> validator,
    IUserProfileReader profiles,
    ICreditCardReader cards,
    ICreditCardStatementReader statements,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListCreditCardStatementsQuery, CreditCardStatementOutput>
{
    public async Task<PaginatedOutput<CreditCardStatementOutput>> HandleAsync(
        ListCreditCardStatementsQuery query)
    {
        var output = PaginatedOutput<CreditCardStatementOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
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

        if (query.From.HasValue)
        {
            var from = query.From.Value;
            filtered = filtered.Where(statement => statement.PeriodStart >= from);
        }

        if (query.To.HasValue)
        {
            var to = query.To.Value;
            filtered = filtered.Where(statement => statement.PeriodEnd <= to);
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

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IOrderedQueryable<CreditCardStatementReadSnapshot> Order(
        IQueryable<CreditCardStatementReadSnapshot> statements,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("periodend", false) => statements.OrderBy(statement => statement.PeriodEnd)
                .ThenBy(statement => statement.Id),
            ("periodend", true) => statements.OrderByDescending(statement => statement.PeriodEnd)
                .ThenByDescending(statement => statement.Id),
            ("closingdate", false) => statements.OrderBy(statement => statement.ClosingDate)
                .ThenBy(statement => statement.Id),
            ("closingdate", true) => statements.OrderByDescending(statement => statement.ClosingDate)
                .ThenByDescending(statement => statement.Id),
            ("duedate", false) => statements.OrderBy(statement => statement.DueDate)
                .ThenBy(statement => statement.Id),
            ("duedate", true) => statements.OrderByDescending(statement => statement.DueDate)
                .ThenByDescending(statement => statement.Id),
            ("status", false) => statements.OrderBy(statement => statement.Status)
                .ThenBy(statement => statement.Id),
            ("status", true) => statements.OrderByDescending(statement => statement.Status)
                .ThenByDescending(statement => statement.Id),
            ("purchasetotal", false) => statements.OrderBy(statement => statement.PurchaseTotal)
                .ThenBy(statement => statement.Id),
            ("purchasetotal", true) => statements.OrderByDescending(statement => statement.PurchaseTotal)
                .ThenByDescending(statement => statement.Id),
            ("amountdue", false) => statements.OrderBy(statement => statement.AmountDue)
                .ThenBy(statement => statement.Id),
            ("amountdue", true) => statements.OrderByDescending(statement => statement.AmountDue)
                .ThenByDescending(statement => statement.Id),
            ("createdat", false) => statements.OrderBy(statement => statement.CreatedAt)
                .ThenBy(statement => statement.Id),
            ("createdat", true) => statements.OrderByDescending(statement => statement.CreatedAt)
                .ThenByDescending(statement => statement.Id),
            ("updatedat", false) => statements.OrderBy(statement => statement.UpdatedAt)
                .ThenBy(statement => statement.Id),
            ("updatedat", true) => statements.OrderByDescending(statement => statement.UpdatedAt)
                .ThenByDescending(statement => statement.Id),
            (_, false) => statements.OrderBy(statement => statement.PeriodStart)
                .ThenBy(statement => statement.Id),
            _ => statements.OrderByDescending(statement => statement.PeriodStart)
                .ThenByDescending(statement => statement.Id)
        };
}
