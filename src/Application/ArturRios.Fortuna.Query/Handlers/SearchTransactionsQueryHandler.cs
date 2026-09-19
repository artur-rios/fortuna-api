using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class SearchTransactionsQueryHandler(
    IValidator<SearchTransactionsQuery> validator,
    ICurrentProfileResolver profileResolver,
    ITransactionReader transactions,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
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

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(TransactionMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(TransactionMessages.CurrencyNotSupported)
                .WithMessage(TransactionMessages.UnknownCurrency(displayCode));
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
        if (!totals.IsFullyConverted)
        {
            output.WithMessage(FigureConversionMessages.PartiallyConverted);
        }

        return output;
    }

    private async Task<TransactionTotalsOutput> BuildTotalsAsync(
        IReadOnlyCollection<TransactionCurrencyTotalSnapshot> snapshots,
        CurrencySnapshot displayCurrency,
        DateOnly figureDate)
    {
        var groups = snapshots.Select(snapshot => new TransactionCurrencyTotalOutput
        {
            CurrencyCode = snapshot.CurrencyCode,
            Expense = snapshot.Expense,
            Earning = snapshot.Earning
        }).ToArray();
        var totals = new TransactionTotalsOutput
        {
            ByCurrency = groups,
            DisplayCurrencyCode = displayCurrency.Code
        };

        // The result set is valued at one figure date, which the caller can choose;
        // every currency group therefore converts at that date.
        var converter = new FigureConverter(rates, displayCurrency);
        var expenses = new List<FigureConversion>(groups.Length);
        var earnings = new List<FigureConversion>(groups.Length);
        foreach (var group in groups)
        {
            var expense = await converter.ConvertAsync(group.CurrencyCode, group.Expense, figureDate);
            var earning = await converter.ConvertAsync(group.CurrencyCode, group.Earning, figureDate);
            expenses.Add(expense);
            earnings.Add(earning);
            group.DisplayCurrencyCode = displayCurrency.Code;
            group.DisplayExpense = converter.Round(expense.Value);
            group.DisplayEarning = converter.Round(earning.Value);
            group.DisplayNet = converter.Round(earning.Value - expense.Value);
            group.AppliedRate = expense.Rate?.Rate;
            group.RateDate = expense.Rate?.RateDate;
            group.RateSource = expense.Rate?.Source;
            group.UnconvertedReason = expense.UnconvertedReason;
        }

        var expenseTotal = converter.Total(expenses);
        var earningTotal = converter.Total(earnings);
        totals.IsFullyConverted = converter.IsFullyConverted;
        totals.MissingRates = MissingExchangeRateOutput.From(converter);
        if (converter.IsFullyConverted)
        {
            totals.DisplayExpense = expenseTotal;
            totals.DisplayEarning = earningTotal;
            totals.DisplayNet = converter.Total(earnings
                .Zip(expenses, (earning, expense) => earning.Value - expense.Value));
        }

        return totals;
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

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static IOrderedQueryable<TransactionReadSnapshot> Order(
        IQueryable<TransactionReadSnapshot> transactions,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "amount" => transactions.SortBy(item => item.Amount, item => item.Id, descending),
            "direction" => transactions.SortBy(item => item.Direction, item => item.Id, descending),
            "category" => transactions
                .SortBy(item => item.CategoryName, item => item.Id, descending),
            "counterparty" => transactions
                .SortBy(item => item.CounterpartyName, item => item.Id, descending),
            "currencycode" => transactions
                .SortBy(item => item.CurrencyCode, item => item.Id, descending),
            "description" => transactions
                .SortBy(item => item.Description, item => item.Id, descending),
            "createdat" => transactions.SortBy(item => item.CreatedAt, item => item.Id, descending),
            "updatedat" => transactions.SortBy(item => item.UpdatedAt, item => item.Id, descending),
            _ => transactions
                .SortBy(item => item.OccurredOn, item => item.CreatedAt, descending)
                .ThenSortBy(item => item.Id, descending)
        };
}
