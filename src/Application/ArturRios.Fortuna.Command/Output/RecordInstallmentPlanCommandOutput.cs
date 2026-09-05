using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordInstallmentPlanCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid CreditCardId { get; set; }
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? OriginalTotalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public short InstallmentCount { get; set; }
    public DateOnly PurchasedOn { get; set; }
    public IReadOnlyCollection<InstallmentCommandOutput> Installments { get; set; } = [];
}

public sealed class InstallmentCommandOutput
{
    public Guid TransactionId { get; set; }
    public short Number { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly OccurredOn { get; set; }
    public Guid? StatementId { get; set; }
    public bool IsLateArriving { get; set; }
}
