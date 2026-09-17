using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListRecurringTransactionsQueryHandler(
    IValidator<ListRecurringTransactionsQuery> validator,
    IUserProfileReader profiles,
    IRecurringTransactionReader rules,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListRecurringTransactionsQuery, RecurringTransactionOutput>
{
    public async Task<PaginatedOutput<RecurringTransactionOutput>> HandleAsync(
        ListRecurringTransactionsQuery query)
    {
        var output = PaginatedOutput<RecurringTransactionOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        }

        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await rules.ListAsync(
            new RecurringTransactionListCriteria(
                profile.Id,
                query.Active,
                query.IncludeDeleted,
                query.SortBy,
                query.Descending,
                query.PageNumber,
                pageSize),
            CancellationToken.None);

        return output
            .WithData(page.Rules.Select(RecurringTransactionProjection.Project).ToList())
            .WithPagination(query.PageNumber, pageSize, page.TotalItems)
            .WithMessage(RecurringTransactionMessages.ListedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
