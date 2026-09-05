using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Transactions;

public enum TransactionDirection : short
{
    Expense = 1,
    Earning = 2
}

public enum TransactionSourceType : short
{
    Manual = 1,
    Pluggy = 2,
    Excel = 3,
    Pdf = 4
}

public sealed class FinancialTransaction : RecordLifecycleEntity
{
    private FinancialTransaction()
    {
    }

    public FinancialTransaction(
        UserProfile user,
        FinancialAccount account,
        Category category,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt,
        string? description = null,
        Counterparty? counterparty = null,
        IReadOnlyCollection<Tag>? tags = null) : this(
            user,
            account,
            null,
            nameof(account),
            category,
            direction,
            amount,
            occurredOn,
            createdAt,
            description,
            counterparty,
            tags)
    {
    }

    public FinancialTransaction(
        UserProfile user,
        CreditCard card,
        Category category,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt,
        string? description = null,
        Counterparty? counterparty = null,
        IReadOnlyCollection<Tag>? tags = null) : this(
            user,
            null,
            card,
            nameof(card),
            category,
            direction,
            amount,
            occurredOn,
            createdAt,
            description,
            counterparty,
            tags)
    {
    }

    private FinancialTransaction(
        UserProfile user,
        FinancialAccount? account,
        CreditCard? card,
        string targetParameterName,
        Category category,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt,
        string? description,
        Counterparty? counterparty,
        IReadOnlyCollection<Tag>? tags) : base(createdAt)
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

        ArgumentNullException.ThrowIfNull(category);
        if (category.User.PublicId != user.PublicId)
        {
            throw new ArgumentException(
                "The transaction and its category must have the same owner.",
                nameof(category));
        }

        if (counterparty is not null && counterparty.User.PublicId != user.PublicId)
        {
            throw new ArgumentException(
                "The transaction and its counterparty must have the same owner.",
                nameof(counterparty));
        }

        if (description?.Trim().Length > 500)
        {
            throw new ArgumentException(
                "A description cannot exceed 500 characters.",
                nameof(description));
        }

        var labels = tags?.DistinctBy(tag => tag.PublicId).ToArray() ?? [];
        if (labels.Any(tag => tag.User.PublicId != user.PublicId))
        {
            throw new ArgumentException(
                "The transaction and its tags must have the same owner.",
                nameof(tags));
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
        Category = category;
        CategoryId = category.Id;
        Counterparty = counterparty;
        CounterpartyId = counterparty?.Id;
        Direction = direction;
        Amount = amount;
        Currency = account?.Currency ?? card!.Currency;
        CurrencyId = Currency.Id;
        OccurredOn = occurredOn;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SourceType = TransactionSourceType.Manual;
        foreach (var tag in labels)
        {
            Tags.Add(tag);
        }
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
    public long CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public long? CounterpartyId { get; private set; }
    public Counterparty? Counterparty { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public decimal? OriginalAmount { get; private set; }
    public long? OriginalCurrencyId { get; private set; }
    public Currency? OriginalCurrency { get; private set; }
    public decimal? AppliedRate { get; private set; }
    public DateOnly? RateDate { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public string? Description { get; private set; }
    public TransactionSourceType SourceType { get; private set; }
    public bool IsReconciled { get; private set; }
    public bool IsLateArriving { get; private set; }
    public ICollection<Tag> Tags { get; } = [];

    public void RecordForeignCurrencyDetails(
        decimal originalAmount,
        Currency originalCurrency,
        decimal appliedRate,
        DateOnly rateDate,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(originalCurrency);
        if (originalAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalAmount));
        }

        if (appliedRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedRate));
        }

        var billedCurrency = FinancialAccount?.Currency ?? CreditCard?.Currency;
        if (billedCurrency is null || originalCurrency.Code == billedCurrency.Code)
        {
            throw new ArgumentException(
                "The original currency must differ from the billed currency.",
                nameof(originalCurrency));
        }

        OriginalAmount = originalAmount;
        OriginalCurrency = originalCurrency;
        OriginalCurrencyId = originalCurrency.Id;
        AppliedRate = appliedRate;
        RateDate = rateDate;
        MarkUpdated(updatedAt);
    }

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
