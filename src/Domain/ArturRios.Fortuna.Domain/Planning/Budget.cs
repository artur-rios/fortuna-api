using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Planning;

public enum BudgetPeriodType : short
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3
}

public sealed class Budget : RecordLifecycleEntity
{
    private Budget()
    {
    }

    public Budget(
        UserProfile user,
        decimal amount,
        Currency currency,
        BudgetPeriodType periodType,
        DateOnly periodStart,
        IReadOnlyCollection<Category> categories,
        bool includeDescendants,
        DateTimeOffset createdAt) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        ValidateDetails(user, amount, periodType, periodStart, categories);

        UserId = user.Id;
        Amount = amount;
        CurrencyId = currency.Id;
        PeriodType = periodType;
        PeriodStart = periodStart;
        IncludeDescendants = includeDescendants;
        Categories = categories.DistinctBy(category => category.PublicId).ToList();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public BudgetPeriodType PeriodType { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public bool IncludeDescendants { get; private set; }
    public ICollection<Category> Categories { get; private set; } = [];

    public void UpdateDetails(
        decimal amount,
        Currency currency,
        BudgetPeriodType periodType,
        DateOnly periodStart,
        IReadOnlyCollection<Category> categories,
        bool includeDescendants,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ValidateDetails(User, amount, periodType, periodStart, categories);

        Amount = amount;
        Currency = currency;
        CurrencyId = currency.Id;
        PeriodType = periodType;
        PeriodStart = periodStart;
        IncludeDescendants = includeDescendants;
        Categories.Clear();
        foreach (var category in categories.DistinctBy(item => item.PublicId))
        {
            Categories.Add(category);
        }

        MarkUpdated(updatedAt);
    }

    private static void ValidateDetails(
        UserProfile user,
        decimal amount,
        BudgetPeriodType periodType,
        DateOnly periodStart,
        IReadOnlyCollection<Category> categories)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!Enum.IsDefined(periodType))
        {
            throw new ArgumentOutOfRangeException(nameof(periodType));
        }

        if (periodStart == default)
        {
            throw new ArgumentException("A period start is required.", nameof(periodStart));
        }

        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Count == 0)
        {
            throw new ArgumentException("At least one category is required.", nameof(categories));
        }

        if (categories.Any(category =>
            category.IsDeleted || category.User.PublicId != user.PublicId))
        {
            throw new ArgumentException(
                "Every category must be live and belong to the budget owner.",
                nameof(categories));
        }
    }
}
