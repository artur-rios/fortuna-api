using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Transactions;

public enum TransactionDirection : short
{
    Expense = 1,
    Earning = 2
}

public sealed class FinancialTransaction : RecordLifecycleEntity
{
    private FinancialTransaction()
    {
    }

    public FinancialTransaction(
        UserProfile user,
        FinancialAccount account,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt) : this(
            user,
            account,
            null,
            nameof(account),
            direction,
            amount,
            occurredOn,
            createdAt)
    {
    }

    public FinancialTransaction(
        UserProfile user,
        CreditCard card,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt) : this(
            user,
            null,
            card,
            nameof(card),
            direction,
            amount,
            occurredOn,
            createdAt)
    {
    }

    private FinancialTransaction(
        UserProfile user,
        FinancialAccount? account,
        CreditCard? card,
        string targetParameterName,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));

        var targetOwner = account?.User ?? card?.User;
        if (targetOwner is null)
        {
            throw new ArgumentException("A transaction target is required.", targetParameterName);
        }

        if (user.PublicId != targetOwner.PublicId)
        {
            throw new ArgumentException(
                "The transaction and its target must have the same owner.",
                targetParameterName);
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A transaction amount must be greater than zero.");
        }

        UserId = user.Id;
        FinancialAccount = account;
        FinancialAccountId = account?.Id;
        CreditCard = card;
        CreditCardId = card?.Id;
        Direction = direction;
        Amount = amount;
        OccurredOn = occurredOn;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public long? FinancialAccountId { get; private set; }
    public FinancialAccount? FinancialAccount { get; private set; }
    public long? CreditCardId { get; private set; }
    public CreditCard? CreditCard { get; private set; }
    public long? StatementId { get; private set; }
    public CreditCardStatement? Statement { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public bool IsLateArriving { get; private set; }

    public void AssignToStatement(
        CreditCardStatement statement,
        bool isLateArriving,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (CreditCard is null || CreditCard.PublicId != statement.CreditCard.PublicId)
        {
            throw new ArgumentException(
                "The transaction and statement must belong to the same credit card.",
                nameof(statement));
        }

        if (statement.Status == CreditCardStatementStatus.Settled)
        {
            throw new InvalidOperationException("A settled statement's composition is frozen.");
        }

        Statement = statement;
        StatementId = statement.Id;
        IsLateArriving = isLateArriving;
        MarkUpdated(updatedAt);
    }
}
