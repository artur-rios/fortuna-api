using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Domain.Cards;

public enum CreditCardStatementStatus : short
{
    Open = 1,
    Closed = 2,
    Settled = 3
}

public sealed record BillingCycle(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly ClosingDate,
    DateOnly DueDate)
{
    public static BillingCycle Containing(DateOnly date, short closingDay, short dueDay)
    {
        ValidateDay(closingDay, nameof(closingDay));
        ValidateDay(dueDay, nameof(dueDay));

        var closingDate = InMonth(date.Year, date.Month, closingDay);
        if (date > closingDate)
        {
            closingDate = InMonth(date.AddMonths(1).Year, date.AddMonths(1).Month, closingDay);
        }

        var previousMonth = closingDate.AddMonths(-1);
        var periodStart = InMonth(previousMonth.Year, previousMonth.Month, closingDay).AddDays(1);
        var dueMonth = dueDay > closingDay ? closingDate : closingDate.AddMonths(1);
        var dueDate = InMonth(dueMonth.Year, dueMonth.Month, dueDay);
        if (dueDate <= closingDate)
        {
            // Clamping a late due day to a short month can land it on (or before) the
            // closing date, e.g. closing 30 / due 31 in April; the bill is then due the
            // following month.
            var followingMonth = dueMonth.AddMonths(1);
            dueDate = InMonth(followingMonth.Year, followingMonth.Month, dueDay);
        }

        return new BillingCycle(periodStart, closingDate, closingDate, dueDate);
    }

    public BillingCycle Next(short closingDay, short dueDay) =>
        Containing(PeriodEnd.AddDays(1), closingDay, dueDay);

    private static DateOnly InMonth(int year, int month, short day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private static void ValidateDay(short day, string parameterName)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class CreditCardStatement : RecordLifecycleEntity
{
    private CreditCardStatement()
    {
    }

    public CreditCardStatement(
        CreditCard creditCard,
        BillingCycle cycle,
        DateTimeOffset createdAt) : base(createdAt)
    {
        CreditCard = creditCard ?? throw new ArgumentNullException(nameof(creditCard));
        CreditCardId = creditCard.Id;
        ArgumentNullException.ThrowIfNull(cycle);
        if (cycle.PeriodStart > cycle.PeriodEnd ||
            cycle.ClosingDate != cycle.PeriodEnd ||
            cycle.DueDate <= cycle.ClosingDate)
        {
            throw new ArgumentException("A valid billing cycle is required.", nameof(cycle));
        }

        PeriodStart = cycle.PeriodStart;
        PeriodEnd = cycle.PeriodEnd;
        ClosingDate = cycle.ClosingDate;
        DueDate = cycle.DueDate;
        Status = CreditCardStatementStatus.Open;
    }

    public long Id { get; private set; }
    public long CreditCardId { get; private set; }
    public CreditCard CreditCard { get; private set; } = null!;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public DateOnly ClosingDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal PreviousBalance { get; private set; }
    public decimal PaymentsReceived { get; private set; }
    public decimal PurchaseTotal { get; private set; }
    public decimal ForeignTaxTotal { get; private set; }
    public decimal OtherEntries { get; private set; }
    public decimal AmountDue { get; private set; }
    public CreditCardStatementStatus Status { get; private set; }
    public long? SettlementTransactionId { get; private set; }
    public FinancialTransaction? SettlementTransaction { get; private set; }

    /// <summary>
    /// Replaces the signed purchase total (refunds count negatively, so a negative total is
    /// legitimate). The amount due moves by the same difference, which keeps any adjustment
    /// carried by an imported invoice summary instead of overwriting it.
    /// </summary>
    public void RecalculatePurchaseTotal(decimal purchaseTotal, DateTimeOffset updatedAt)
    {
        EnsureCompositionOpen();
        AmountDue += purchaseTotal - PurchaseTotal;
        PurchaseTotal = purchaseTotal;
        MarkUpdated(updatedAt);
    }

    /// <summary>Closes an open statement; closing an already closed statement is a no-op.</summary>
    public void Close(DateTimeOffset updatedAt)
    {
        EnsureCompositionOpen();
        if (Status == CreditCardStatementStatus.Open)
        {
            Status = CreditCardStatementStatus.Closed;
            MarkUpdated(updatedAt);
        }
    }

    /// <summary>
    /// Replaces the balance carried from earlier cycles. A negative value is a credit balance,
    /// which imported invoice summaries may also report.
    /// </summary>
    public void SetPreviousBalance(decimal previousBalance, DateTimeOffset updatedAt)
    {
        EnsureCompositionOpen();
        AmountDue += previousBalance - PreviousBalance;
        PreviousBalance = previousBalance;
        MarkUpdated(updatedAt);
    }

    public void ApplyImportedSummary(
        decimal previousBalance,
        decimal paymentsReceived,
        decimal purchaseTotal,
        decimal foreignTaxTotal,
        decimal otherEntries,
        decimal amountDue,
        DateTimeOffset updatedAt)
    {
        EnsureCompositionOpen();
        if (paymentsReceived < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentsReceived));
        }

        var calculatedAmountDue = previousBalance - paymentsReceived + purchaseTotal +
            foreignTaxTotal + otherEntries;
        if (Math.Abs(calculatedAmountDue - amountDue) > Money.MinorUnit(CreditCard.Currency))
        {
            throw new ArgumentException(
                "The imported statement summary does not reconcile.",
                nameof(amountDue));
        }

        PreviousBalance = previousBalance;
        PaymentsReceived = paymentsReceived;
        PurchaseTotal = purchaseTotal;
        ForeignTaxTotal = foreignTaxTotal;
        OtherEntries = otherEntries;
        AmountDue = amountDue;
        MarkUpdated(updatedAt);
    }

    public void Settle(FinancialTransaction settlementTransaction, DateTimeOffset updatedAt)
    {
        if (Status != CreditCardStatementStatus.Closed)
        {
            throw new InvalidOperationException("Only a closed statement can be settled.");
        }

        ArgumentNullException.ThrowIfNull(settlementTransaction);
        if (settlementTransaction.CreditCard?.PublicId != CreditCard.PublicId ||
            settlementTransaction.Direction != TransactionDirection.Earning)
        {
            throw new ArgumentException(
                "A statement settlement must be an inbound movement on its credit card.",
                nameof(settlementTransaction));
        }

        SettlementTransaction = settlementTransaction;
        SettlementTransactionId = settlementTransaction.Id;
        Status = CreditCardStatementStatus.Settled;
        MarkUpdated(updatedAt);
    }

    private void EnsureCompositionOpen()
    {
        if (Status == CreditCardStatementStatus.Settled)
        {
            throw new InvalidOperationException("A settled statement's composition is frozen.");
        }
    }
}
