using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class SearchTransactionsQueryHandler(
    IValidator<SearchTransactionsQuery> validator,
    IUserProfileReader profiles,
    ITransactionReader transactions,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<SearchTransactionsQuery, TransactionSearchOutput>
{
    public async Task<DataOutput<TransactionSearchOutput?>> HandleAsync(
        SearchTransactionsQuery query)
    {
        var output = DataOutput<TransactionSearchOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(TransactionMessages.ProfileNotFound);
        }

        var displayCurrency = await ResolveDisplayCurrencyAsync(query.DisplayCurrencyCode);
        if (!string.IsNullOrWhiteSpace(query.DisplayCurrencyCode) && displayCurrency is null)
        {
            var code = query.DisplayCurrencyCode.Trim().ToUpperInvariant();
            return output
                .WithError(TransactionMessages.CurrencyNotSupported)
                .WithMessage(TransactionMessages.UnknownCurrency(code));
        }

        var criteria = Criteria(profile.Id, query);
        var ordered = Order(transactions.Query(criteria), query.SortBy.Trim(), query.Descending);
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await ordered.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);
        var aggregateSnapshots = await transactions.SummarizeAsync(
            criteria,
            CancellationToken.None);
        var totals = await BuildTotalsAsync(
            aggregateSnapshots,
            displayCurrency,
            query.FigureDate ?? Today());
        var result = new TransactionSearchOutput
        {
            Items = (page.Data ?? []).Select(TransactionProjection.Project).ToArray(),
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems,
            Totals = totals
        };
        output.WithData(result).WithMessage(TransactionMessages.ListedSuccessfully);
        if (totals.ByCurrency.Any(total => total.UnconvertedReason is not null))
        {
            output.WithMessage(FigureConversionMessages.PartiallyConverted);
        }

        return output;
    }

    private async Task<TransactionTotalsOutput> BuildTotalsAsync(
        IReadOnlyCollection<TransactionCurrencyTotalSnapshot> snapshots,
        CurrencySnapshot? displayCurrency,
        DateOnly figureDate)
    {
        var groups = new List<TransactionCurrencyTotalOutput>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var group = new TransactionCurrencyTotalOutput
            {
                CurrencyCode = snapshot.CurrencyCode,
                Expense = snapshot.Expense,
                Earning = snapshot.Earning
            };
            groups.Add(group);
            if (displayCurrency is null)
            {
                continue;
            }

            group.DisplayCurrencyCode = displayCurrency.Code;
            if (snapshot.CurrencyCode == displayCurrency.Code)
            {
                ApplyConversion(group, 1m, displayCurrency.MinorUnitDigits);
                continue;
            }

            var rate = await rates.FindApplicableAsync(
                snapshot.CurrencyCode,
                displayCurrency.Code,
                figureDate,
                CancellationToken.None);
            if (rate is null)
            {
                group.UnconvertedReason = FigureConversionMessages.RateUnavailable;
                continue;
            }

            ApplyConversion(group, rate.Rate, displayCurrency.MinorUnitDigits);
            group.AppliedRate = rate.Rate;
            group.RateDate = rate.RateDate;
            group.RateSource = rate.Source;
        }

        var totals = new TransactionTotalsOutput
        {
            ByCurrency = groups,
            DisplayCurrencyCode = displayCurrency?.Code
        };
        if (displayCurrency is not null && groups.All(group => group.UnconvertedReason is null))
        {
            totals.DisplayExpense = groups.Sum(group => group.DisplayExpense ?? 0m);
            totals.DisplayEarning = groups.Sum(group => group.DisplayEarning ?? 0m);
            totals.DisplayNet = totals.DisplayEarning - totals.DisplayExpense;
        }

        return totals;
    }

    private static void ApplyConversion(
        TransactionCurrencyTotalOutput group,
        decimal rate,
        short minorUnitDigits)
    {
        group.DisplayExpense = Round(group.Expense * rate, minorUnitDigits);
        group.DisplayEarning = Round(group.Earning * rate, minorUnitDigits);
        group.DisplayNet = group.DisplayEarning - group.DisplayExpense;
    }

    private static TransactionSearchCriteria Criteria(
        Guid userId,
        SearchTransactionsQuery query) => new()
        {
            UserId = userId,
            From = query.From,
            To = query.To,
            FinancialAccountId = query.FinancialAccountId,
            CreditCardId = query.CreditCardId,
            CategoryId = query.CategoryId,
            TagId = query.TagId,
            CounterpartyId = query.CounterpartyId,
            Direction = query.Direction,
            MinimumAmount = query.MinimumAmount,
            MaximumAmount = query.MaximumAmount,
            Text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            IncludeDeleted = query.IncludeDeleted
        };

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

    private static decimal Round(decimal amount, short digits) =>
        decimal.Round(amount, digits, MidpointRounding.AwayFromZero);

    private static IOrderedQueryable<TransactionReadSnapshot> Order(
        IQueryable<TransactionReadSnapshot> transactions,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("amount", false) => transactions.OrderBy(item => item.Amount).ThenBy(item => item.Id),
            ("amount", true) => transactions.OrderByDescending(item => item.Amount)
                .ThenByDescending(item => item.Id),
            ("direction", false) => transactions.OrderBy(item => item.Direction)
                .ThenBy(item => item.Id),
            ("direction", true) => transactions.OrderByDescending(item => item.Direction)
                .ThenByDescending(item => item.Id),
            ("category", false) => transactions.OrderBy(item => item.CategoryName)
                .ThenBy(item => item.Id),
            ("category", true) => transactions.OrderByDescending(item => item.CategoryName)
                .ThenByDescending(item => item.Id),
            ("counterparty", false) => transactions.OrderBy(item => item.CounterpartyName)
                .ThenBy(item => item.Id),
            ("counterparty", true) => transactions.OrderByDescending(item => item.CounterpartyName)
                .ThenByDescending(item => item.Id),
            ("currencycode", false) => transactions.OrderBy(item => item.CurrencyCode)
                .ThenBy(item => item.Id),
            ("currencycode", true) => transactions.OrderByDescending(item => item.CurrencyCode)
                .ThenByDescending(item => item.Id),
            ("description", false) => transactions.OrderBy(item => item.Description)
                .ThenBy(item => item.Id),
            ("description", true) => transactions.OrderByDescending(item => item.Description)
                .ThenByDescending(item => item.Id),
            ("createdat", false) => transactions.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id),
            ("createdat", true) => transactions.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id),
            ("updatedat", false) => transactions.OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            ("updatedat", true) => transactions.OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id),
            (_, false) => transactions.OrderBy(item => item.OccurredOn)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id),
            _ => transactions.OrderByDescending(item => item.OccurredOn)
                .ThenByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
        };
}
