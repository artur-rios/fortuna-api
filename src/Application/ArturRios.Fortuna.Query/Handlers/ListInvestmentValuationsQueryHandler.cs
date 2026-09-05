using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListInvestmentValuationsQueryHandler(
    IValidator<ListInvestmentValuationsQuery> validator,
    IUserProfileReader profiles,
    IInvestmentReader investments,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListInvestmentValuationsQuery, InvestmentValuationOutput>
{
    public async Task<PaginatedOutput<InvestmentValuationOutput>> HandleAsync(
        ListInvestmentValuationsQuery query)
    {
        var output = PaginatedOutput<InvestmentValuationOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        if (await investments.FindByIdWithPositionAsync(
            profile.Id,
            query.InvestmentId,
            CancellationToken.None) is null)
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

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IOrderedQueryable<InvestmentValuationReadSnapshot> Order(
        IQueryable<InvestmentValuationReadSnapshot> valuations,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("value", false) => valuations.OrderBy(item => item.Value).ThenBy(item => item.Id),
            ("value", true) => valuations.OrderByDescending(item => item.Value)
                .ThenByDescending(item => item.Id),
            ("createdat", false) => valuations.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id),
            ("createdat", true) => valuations.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id),
            ("updatedat", false) => valuations.OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            ("updatedat", true) => valuations.OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id),
            (_, false) => valuations.OrderBy(item => item.ValuedOn).ThenBy(item => item.Id),
            _ => valuations.OrderByDescending(item => item.ValuedOn)
                .ThenByDescending(item => item.Id)
        };
}
