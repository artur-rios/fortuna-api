using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Planning;

public sealed class Goal : RecordLifecycleEntity
{
    private Goal()
    {
    }

    public Goal(
        UserProfile user,
        string name,
        decimal targetAmount,
        Currency currency,
        DateOnly targetDate,
        IReadOnlyCollection<FinancialAccount> accounts,
        IReadOnlyCollection<Investment> investments,
        DateTimeOffset createdAt) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        ValidateDetails(user, name, targetAmount, targetDate, accounts, investments, createdAt);

        UserId = user.Id;
        Name = name.Trim();
        TargetAmount = targetAmount;
        CurrencyId = currency.Id;
        TargetDate = targetDate;
        Accounts = accounts.DistinctBy(item => item.PublicId).ToList();
        Investments = investments.DistinctBy(item => item.PublicId).ToList();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public decimal TargetAmount { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public DateOnly TargetDate { get; private set; }
    public ICollection<FinancialAccount> Accounts { get; private set; } = [];
    public ICollection<Investment> Investments { get; private set; } = [];

    public void UpdateDetails(
        string name,
        decimal targetAmount,
        Currency currency,
        DateOnly targetDate,
        IReadOnlyCollection<FinancialAccount> accounts,
        IReadOnlyCollection<Investment> investments,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ValidateDetails(User, name, targetAmount, targetDate, accounts, investments, updatedAt);

        Name = name.Trim();
        TargetAmount = targetAmount;
        Currency = currency;
        CurrencyId = currency.Id;
        TargetDate = targetDate;
        Accounts.Clear();
        foreach (var account in accounts.DistinctBy(item => item.PublicId))
        {
            Accounts.Add(account);
        }

        Investments.Clear();
        foreach (var investment in investments.DistinctBy(item => item.PublicId))
        {
            Investments.Add(investment);
        }

        MarkUpdated(updatedAt);
    }

    private static void ValidateDetails(
        UserProfile user,
        string name,
        decimal targetAmount,
        DateOnly targetDate,
        IReadOnlyCollection<FinancialAccount> accounts,
        IReadOnlyCollection<Investment> investments,
        DateTimeOffset changedAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A goal name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (targetAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAmount));
        }

        if (targetDate <= DateOnly.FromDateTime(changedAt.UtcDateTime))
        {
            throw new ArgumentOutOfRangeException(nameof(targetDate));
        }

        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(investments);
        if (accounts.Count == 0 && investments.Count == 0)
        {
            throw new ArgumentException(
                "At least one account or investment is required.",
                nameof(accounts));
        }

        if (accounts.Any(item => item.IsDeleted || item.User.PublicId != user.PublicId))
        {
            throw new ArgumentException(
                "Every account must be live and belong to the goal owner.",
                nameof(accounts));
        }

        if (investments.Any(item => item.IsDeleted || item.User.PublicId != user.PublicId))
        {
            throw new ArgumentException(
                "Every investment must be live and belong to the goal owner.",
                nameof(investments));
        }
    }
}
