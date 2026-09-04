using ArturRios.Fortuna.Domain.Accounts;
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
        DateTimeOffset createdAt) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        FinancialAccount = account ?? throw new ArgumentNullException(nameof(account));

        if (user.PublicId != account.User.PublicId)
        {
            throw new ArgumentException(
                "The transaction and financial account must have the same owner.",
                nameof(account));
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
        FinancialAccountId = account.Id;
        Direction = direction;
        Amount = amount;
        OccurredOn = occurredOn;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public long FinancialAccountId { get; private set; }
    public FinancialAccount FinancialAccount { get; private set; } = null!;
    public TransactionDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly OccurredOn { get; private set; }
}
