using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TransactionSearchOutput : QueryOutput
{
    public IReadOnlyCollection<TransactionOutput> Items { get; set; } = [];
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize == 0
        ? 0
        : (int)Math.Ceiling((decimal)TotalItems / PageSize);
    public TransactionTotalsOutput Totals { get; set; } = new();
}

public sealed class TransactionTotalsOutput
{
    public IReadOnlyCollection<TransactionCurrencyTotalOutput> ByCurrency { get; set; } = [];
    public string? DisplayCurrencyCode { get; set; }
    public decimal? DisplayExpense { get; set; }
    public decimal? DisplayEarning { get; set; }
    public decimal? DisplayNet { get; set; }
}

public sealed class TransactionCurrencyTotalOutput
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Expense { get; set; }
    public decimal Earning { get; set; }
    public decimal Net => Earning - Expense;
    public string? DisplayCurrencyCode { get; set; }
    public decimal? DisplayExpense { get; set; }
    public decimal? DisplayEarning { get; set; }
    public decimal? DisplayNet { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
