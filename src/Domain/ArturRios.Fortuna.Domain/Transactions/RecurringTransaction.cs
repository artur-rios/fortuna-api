using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Transactions;

public enum RecurrenceFrequency : short
{
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4
}

public sealed class RecurringTransaction : RecordLifecycleEntity
{
    private RecurringTransaction()
    {
    }

    public RecurringTransaction(
        UserProfile user,
        FinancialAccount? financialAccount,
        CreditCard? creditCard,
        Category category,
        TransactionDirection direction,
        decimal amount,
        RecurrenceFrequency frequency,
        DateOnly startsOn,
        DateOnly? endsOn,
        DateTimeOffset createdAt,
        string? description = null,
        Counterparty? counterparty = null) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        ArgumentNullException.ThrowIfNull(category);
        if ((financialAccount is null) == (creditCard is null))
        {
            throw new ArgumentException("Exactly one transaction target is required.");
        }

        var targetUser = financialAccount?.User ?? creditCard!.User;
        if (targetUser.PublicId != user.PublicId || category.User.PublicId != user.PublicId ||
            (counterparty is not null && counterparty.User.PublicId != user.PublicId))
        {
            throw new ArgumentException("The recurring transaction references must share an owner.");
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (!Enum.IsDefined(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (endsOn < startsOn)
        {
            throw new ArgumentOutOfRangeException(nameof(endsOn));
        }

        if (description?.Trim().Length > 500)
        {
            throw new ArgumentException("A description cannot exceed 500 characters.", nameof(description));
        }

        UserId = user.Id;
        FinancialAccount = financialAccount;
        FinancialAccountId = financialAccount?.Id;
        CreditCard = creditCard;
        CreditCardId = creditCard?.Id;
        Category = category;
        CategoryId = category.Id;
        Counterparty = counterparty;
        CounterpartyId = counterparty?.Id;
        Direction = direction;
        Amount = amount;
        Currency = financialAccount?.Currency ?? creditCard!.Currency;
        CurrencyId = Currency.Id;
        Frequency = frequency;
        StartsOn = startsOn;
        EndsOn = endsOn;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public long? FinancialAccountId { get; private set; }
    public FinancialAccount? FinancialAccount { get; private set; }
    public long? CreditCardId { get; private set; }
    public CreditCard? CreditCard { get; private set; }
    public long CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public long? CounterpartyId { get; private set; }
    public Counterparty? Counterparty { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public RecurrenceFrequency Frequency { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }
    public DateOnly? LastMaterializedOn { get; private set; }
    public string? Description { get; private set; }

    public DateOnly OccurrenceAt(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Frequency switch
        {
            RecurrenceFrequency.Weekly => StartsOn.AddDays(index * 7),
            RecurrenceFrequency.Monthly => StartsOn.AddMonths(index),
            RecurrenceFrequency.Quarterly => StartsOn.AddMonths(index * 3),
            RecurrenceFrequency.Yearly => StartsOn.AddYears(index),
            _ => throw new InvalidOperationException("Unsupported recurrence frequency.")
        };
    }

    public IReadOnlyCollection<DateOnly> NextOccurrences(DateOnly from, int count = 5)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var dates = new List<DateOnly>(count);
        for (var index = 0; dates.Count < count; index++)
        {
            var occurrence = OccurrenceAt(index);
            if (EndsOn.HasValue && occurrence > EndsOn.Value)
            {
                break;
            }

            if (occurrence >= from)
            {
                dates.Add(occurrence);
            }
        }

        return dates;
    }
}
