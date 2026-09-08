using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public enum CashFlowPeriodicity
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public sealed class ProjectCashFlowQuery : BaseQuery
{
    public int HorizonDays { get; set; }
    public string? DisplayCurrencyCode { get; set; }
    public bool IncludeEstimate { get; set; }
    public CashFlowPeriodicity Periodicity { get; set; } = CashFlowPeriodicity.Monthly;
}
