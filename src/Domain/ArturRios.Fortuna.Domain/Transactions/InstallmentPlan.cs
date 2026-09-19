using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Domain.Transactions;

public sealed class InstallmentPlan : RecordLifecycleEntity
{
    private readonly List<FinancialTransaction> _installments = [];

    private InstallmentPlan()
    {
    }

    public InstallmentPlan(
        CreditCard creditCard,
        decimal totalAmount,
        short installmentCount,
        DateOnly purchasedOn,
        DateTimeOffset createdAt) : base(createdAt)
    {
        CreditCard = creditCard ?? throw new ArgumentNullException(nameof(creditCard));
        if (totalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount));
        }

        if (installmentCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentCount));
        }

        CreditCardId = creditCard.Id;
        TotalAmount = totalAmount;
        InstallmentCount = installmentCount;
        PurchasedOn = purchasedOn;
    }

    public long Id { get; private set; }
    public long CreditCardId { get; private set; }
    public CreditCard CreditCard { get; private set; } = null!;
    public decimal TotalAmount { get; private set; }
    public short InstallmentCount { get; private set; }
    public DateOnly PurchasedOn { get; private set; }
    public IReadOnlyCollection<FinancialTransaction> Installments => _installments;

    public void AddInstallment(
        FinancialTransaction transaction,
        short installmentNumber,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.CreditCard?.PublicId != CreditCard.PublicId)
        {
            throw new ArgumentException(
                "An installment must belong to the plan's credit card.",
                nameof(transaction));
        }

        if (transaction.Direction != TransactionDirection.Expense)
        {
            throw new ArgumentException("An installment must be an expense.", nameof(transaction));
        }

        if (installmentNumber < 1 || installmentNumber > InstallmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(installmentNumber));
        }

        if (Installments.Any(item => item.InstallmentNumber == installmentNumber))
        {
            throw new ArgumentException(
                "An installment with this number already exists.",
                nameof(installmentNumber));
        }

        transaction.AssignToInstallmentPlan(this, installmentNumber, updatedAt);
        _installments.Add(transaction);
    }

    public static InstallmentSplit TrySplit(
        decimal totalAmount,
        short installmentCount,
        short minorUnitDigits)
    {
        if (totalAmount <= 0m)
        {
            return InstallmentSplit.Refused(InstallmentSplitOutcome.TotalNotPositive);
        }

        if (installmentCount < 2)
        {
            return InstallmentSplit.Refused(InstallmentSplitOutcome.TooFewInstallments);
        }

        if (minorUnitDigits is < 0 or > 4)
        {
            return InstallmentSplit.Refused(InstallmentSplitOutcome.MinorUnitDigitsUnsupported);
        }

        // A total finer than the currency's minor unit would leak the excess into the first
        // installment, so the total is brought to the currency scale before splitting.
        totalAmount = decimal.Round(totalAmount, minorUnitDigits, MidpointRounding.AwayFromZero);
        var regularAmount = decimal.Round(
            totalAmount / installmentCount,
            minorUnitDigits,
            MidpointRounding.ToZero);
        if (regularAmount <= 0m)
        {
            return InstallmentSplit.Refused(InstallmentSplitOutcome.AmountTooSmall);
        }

        var firstAmount = totalAmount - regularAmount * (installmentCount - 1);
        var amounts = Enumerable.Repeat(regularAmount, installmentCount).ToArray();
        amounts[0] = firstAmount;

        return new InstallmentSplit(InstallmentSplitOutcome.Succeeded, amounts);
    }
}

public enum InstallmentSplitOutcome
{
    Succeeded = 1,
    TotalNotPositive = 2,
    TooFewInstallments = 3,
    MinorUnitDigitsUnsupported = 4,
    AmountTooSmall = 5
}

public sealed record InstallmentSplit(
    InstallmentSplitOutcome Outcome,
    IReadOnlyList<decimal> Amounts)
{
    public bool Succeeded => Outcome == InstallmentSplitOutcome.Succeeded;

    public static InstallmentSplit Refused(InstallmentSplitOutcome outcome) => new(outcome, []);
}
