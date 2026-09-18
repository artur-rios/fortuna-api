using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Guards;
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
        UserId = user.Id;
        ApplyTemplate(
            financialAccount,
            creditCard,
            category,
            direction,
            amount,
            frequency,
            startsOn,
            endsOn,
            description,
            counterparty);
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

    public void UpdateTemplate(
        FinancialAccount? financialAccount,
        CreditCard? creditCard,
        Category category,
        TransactionDirection direction,
        decimal amount,
        RecurrenceFrequency frequency,
        DateOnly startsOn,
        DateOnly? endsOn,
        string? description,
        Counterparty? counterparty,
        DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        var previousStart = StartsOn;
        ApplyTemplate(
            financialAccount,
            creditCard,
            category,
            direction,
            amount,
            frequency,
            startsOn,
            endsOn,
            description,
            counterparty);
        if (startsOn < previousStart)
        {
            // Occurrences between the new and the old start were never materialized; the
            // marker would make materialization skip them, so it restarts from the new start.
            // Occurrences that already exist are recognized and not duplicated.
            LastMaterializedOn = null;
        }

        MarkUpdated(updatedAt);
    }

    public IReadOnlyCollection<DateOnly> OccurrencesBetween(DateOnly from, DateOnly through)
    {
        if (through < from || through < StartsOn)
        {
            return [];
        }

        var dates = new List<DateOnly>();
        for (var index = 0; ; index++)
        {
            var occurrence = OccurrenceAt(index);
            if (occurrence > through || EndsOn.HasValue && occurrence > EndsOn.Value)
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

    public bool IsCompleteOn(DateOnly date)
    {
        if (!EndsOn.HasValue)
        {
            return false;
        }

        return !NextOccurrences(date.AddDays(1), 1).Any();
    }

    public void MarkMaterializedThrough(DateOnly occurrence, DateTimeOffset updatedAt)
    {
        if (occurrence < StartsOn || EndsOn.HasValue && occurrence > EndsOn.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence));
        }

        if (!OccurrencesBetween(occurrence, occurrence).Contains(occurrence))
        {
            throw new ArgumentException("The materialization marker must be an occurrence date.", nameof(occurrence));
        }

        if (!LastMaterializedOn.HasValue || occurrence > LastMaterializedOn.Value)
        {
            LastMaterializedOn = occurrence;
            MarkUpdated(updatedAt);
        }
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

    private void ApplyTemplate(
        FinancialAccount? financialAccount,
        CreditCard? creditCard,
        Category category,
        TransactionDirection direction,
        decimal amount,
        RecurrenceFrequency frequency,
        DateOnly startsOn,
        DateOnly? endsOn,
        string? description,
        Counterparty? counterparty)
    {
        ArgumentNullException.ThrowIfNull(category);
        var target = TransactionTarget.Of(financialAccount, creditCard);
        if (target.Owner.PublicId != User.PublicId || category.User.PublicId != User.PublicId ||
            (counterparty is not null && counterparty.User.PublicId != User.PublicId))
        {
            throw new ArgumentException("The recurring transaction references must share an owner.");
        }

        if (target.IsDeleted || category.IsDeleted || counterparty?.IsDeleted == true)
        {
            throw new ArgumentException("The recurring transaction references must be live.");
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

        Description = BoundedText.Optional(
            description,
            500,
            nameof(description),
            "A description cannot exceed 500 characters.");
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
        Currency = target.Currency;
        CurrencyId = Currency.Id;
        Frequency = frequency;
        StartsOn = startsOn;
        EndsOn = endsOn;
    }
}
