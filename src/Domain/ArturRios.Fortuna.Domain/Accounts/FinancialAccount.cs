using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Accounts;

public enum FinancialAccountType : short
{
    Checking = 1,
    Savings = 2,
    Cash = 3,
    Other = 4
}

public sealed class FinancialAccount : RecordLifecycleEntity
{
    private FinancialAccount()
    {
    }

    public FinancialAccount(
        UserProfile user,
        string name,
        string? institution,
        FinancialAccountType accountType,
        Currency currency,
        decimal openingBalance,
        DateTimeOffset createdAt) : base(createdAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An account name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (institution?.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An institution cannot exceed 200 characters.",
                nameof(institution));
        }

        if (!Enum.IsDefined(accountType))
        {
            throw new ArgumentOutOfRangeException(nameof(accountType));
        }

        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Institution = string.IsNullOrWhiteSpace(institution) ? null : institution.Trim();
        AccountType = accountType;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        CurrencyId = currency.Id;
        OpeningBalance = openingBalance;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Institution { get; private set; }
    public FinancialAccountType AccountType { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public decimal OpeningBalance { get; private set; }
}
