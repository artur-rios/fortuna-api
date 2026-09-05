using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Domain.Transactions;

public sealed class Transfer : RecordLifecycleEntity
{
    private Transfer()
    {
    }

    public Transfer(
        FinancialTransaction outboundTransaction,
        FinancialTransaction inboundTransaction,
        decimal? appliedRate,
        DateOnly? rateDate,
        DateTimeOffset createdAt) : base(createdAt)
    {
        OutboundTransaction = outboundTransaction ??
            throw new ArgumentNullException(nameof(outboundTransaction));
        InboundTransaction = inboundTransaction ??
            throw new ArgumentNullException(nameof(inboundTransaction));
        if (ReferenceEquals(outboundTransaction, inboundTransaction))
        {
            throw new ArgumentException(
                "A transfer requires different outbound and inbound transactions.",
                nameof(inboundTransaction));
        }

        if (outboundTransaction.User.PublicId != inboundTransaction.User.PublicId)
        {
            throw new ArgumentException(
                "Both transfer movements must have the same owner.",
                nameof(inboundTransaction));
        }

        if (outboundTransaction.Direction != TransactionDirection.Expense ||
            inboundTransaction.Direction != TransactionDirection.Earning)
        {
            throw new ArgumentException("A transfer requires an outbound and an inbound movement.");
        }

        ValidateConversion(appliedRate, rateDate);

        OutboundTransactionId = outboundTransaction.Id;
        InboundTransactionId = inboundTransaction.Id;
        AppliedRate = appliedRate;
        RateDate = rateDate;
    }

    public Transfer(
        FinancialTransaction outboundTransaction,
        InvestmentMovement inboundInvestmentMovement,
        decimal? appliedRate,
        DateOnly? rateDate,
        DateTimeOffset createdAt) : base(createdAt)
    {
        OutboundTransaction = outboundTransaction ??
            throw new ArgumentNullException(nameof(outboundTransaction));
        InboundInvestmentMovement = inboundInvestmentMovement ??
            throw new ArgumentNullException(nameof(inboundInvestmentMovement));
        if (outboundTransaction.User.PublicId !=
            inboundInvestmentMovement.Investment.User.PublicId)
        {
            throw new ArgumentException(
                "Both transfer movements must have the same owner.",
                nameof(inboundInvestmentMovement));
        }

        if (outboundTransaction.Direction != TransactionDirection.Expense ||
            inboundInvestmentMovement.MovementType != InvestmentMovementType.Contribution)
        {
            throw new ArgumentException(
                "An investment transfer requires an outbound movement and a contribution.");
        }

        ValidateConversion(appliedRate, rateDate);
        OutboundTransactionId = outboundTransaction.Id;
        InboundInvestmentMovementId = inboundInvestmentMovement.Id;
        AppliedRate = appliedRate;
        RateDate = rateDate;
    }

    public long Id { get; private set; }
    public long OutboundTransactionId { get; private set; }
    public FinancialTransaction OutboundTransaction { get; private set; } = null!;
    public long? InboundTransactionId { get; private set; }
    public FinancialTransaction? InboundTransaction { get; private set; }
    public long? InboundInvestmentMovementId { get; private set; }
    public InvestmentMovement? InboundInvestmentMovement { get; private set; }
    public decimal? AppliedRate { get; private set; }
    public DateOnly? RateDate { get; private set; }

    private static void ValidateConversion(decimal? appliedRate, DateOnly? rateDate)
    {
        if (appliedRate.HasValue != rateDate.HasValue || appliedRate is <= 0m)
        {
            throw new ArgumentException(
                "A positive applied rate and its date must be supplied together.",
                nameof(appliedRate));
        }
    }
}
