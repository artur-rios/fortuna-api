using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListInvestmentsQueryHandler(
    IValidator<ListInvestmentsQuery> validator,
    IUserProfileReader profiles,
    IInvestmentReader investments,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions,
    TimeProvider timeProvider)
    : IPaginatedQueryHandlerAsync<ListInvestmentsQuery, InvestmentOutput>
{
    public async Task<PaginatedOutput<InvestmentOutput>> HandleAsync(ListInvestmentsQuery query)
    {
        var output = PaginatedOutput<InvestmentOutput>.New;
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

        var displayCurrency = await ResolveDisplayCurrencyAsync(query.DisplayCurrencyCode);
        if (!string.IsNullOrWhiteSpace(query.DisplayCurrencyCode) && displayCurrency is null)
        {
            var code = query.DisplayCurrencyCode.Trim().ToUpperInvariant();
            return output
                .WithError(InvestmentMessages.CurrencyNotSupported)
                .WithMessage(InvestmentMessages.UnknownCurrency(code));
        }

        var filtered = investments.QueryPositions().Where(investment =>
            investment.UserId == profile.Id && !investment.IsDeleted);
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
        foreach (var investment in page.Data ?? [])
        {
            await InvestmentPositionProjection.ApplyConversionAsync(
                investment,
                displayCurrency,
                query.FigureDate ?? Today(),
                rates);
        }

        return page.WithMessage(InvestmentMessages.ListedSuccessfully);
    }

    private async Task<CurrencySnapshot?> ResolveDisplayCurrencyAsync(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : await currencies.FindByCodeAsync(
                code.Trim().ToUpperInvariant(),
                CancellationToken.None);

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static IOrderedQueryable<InvestmentPositionSnapshot> Order(
        IQueryable<InvestmentPositionSnapshot> investments,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("institution", false) => investments.OrderBy(item => item.Institution)
                .ThenBy(item => item.Id),
            ("institution", true) => investments.OrderByDescending(item => item.Institution)
                .ThenByDescending(item => item.Id),
            ("investmenttype", false) => investments.OrderBy(item => item.InvestmentType)
                .ThenBy(item => item.Id),
            ("investmenttype", true) => investments.OrderByDescending(item => item.InvestmentType)
                .ThenByDescending(item => item.Id),
            ("currencycode", false) => investments.OrderBy(item => item.CurrencyCode)
                .ThenBy(item => item.Id),
            ("currencycode", true) => investments.OrderByDescending(item => item.CurrencyCode)
                .ThenByDescending(item => item.Id),
            ("position", false) => investments.OrderBy(item => item.Position)
                .ThenBy(item => item.Id),
            ("position", true) => investments.OrderByDescending(item => item.Position)
                .ThenByDescending(item => item.Id),
            ("createdat", false) => investments.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id),
            ("createdat", true) => investments.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id),
            ("updatedat", false) => investments.OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            ("updatedat", true) => investments.OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id),
            (_, false) => investments.OrderBy(item => item.Instrument)
                .ThenBy(item => item.Id),
            _ => investments.OrderByDescending(item => item.Instrument)
                .ThenByDescending(item => item.Id)
        };
}
