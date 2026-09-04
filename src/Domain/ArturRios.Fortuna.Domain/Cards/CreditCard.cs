using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Cards;

public sealed class CreditCard : RecordLifecycleEntity
{
    private CreditCard()
    {
    }

    public CreditCard(
        UserProfile user,
        string name,
        string issuer,
        Currency currency,
        decimal creditLimit,
        short closingDay,
        short dueDay,
        string? lastFourDigits,
        DateTimeOffset createdAt) : base(createdAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A card name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(issuer) || issuer.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A card issuer between 1 and 200 characters is required.",
                nameof(issuer));
        }

        if (creditLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditLimit));
        }

        if (closingDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(closingDay));
        }

        if (dueDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dueDay));
        }

        var digits = string.IsNullOrWhiteSpace(lastFourDigits) ? null : lastFourDigits.Trim();
        if (digits is not null && (digits.Length != 4 || digits.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException(
                "The last four digits must contain exactly four numeric digits.",
                nameof(lastFourDigits));
        }

        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Issuer = issuer.Trim();
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        CurrencyId = currency.Id;
        CreditLimit = creditLimit;
        ClosingDay = closingDay;
        DueDay = dueDay;
        LastFourDigits = digits;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Issuer { get; private set; } = string.Empty;
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public decimal CreditLimit { get; private set; }
    public short ClosingDay { get; private set; }
    public short DueDay { get; private set; }
    public string? LastFourDigits { get; private set; }

    public void UpdateDetails(
        string name,
        string issuer,
        decimal creditLimit,
        short closingDay,
        short dueDay,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A card name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(issuer) || issuer.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A card issuer between 1 and 200 characters is required.",
                nameof(issuer));
        }

        if (creditLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditLimit));
        }

        if (closingDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(closingDay));
        }

        if (dueDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dueDay));
        }

        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Issuer = issuer.Trim();
        CreditLimit = creditLimit;
        ClosingDay = closingDay;
        DueDay = dueDay;
        MarkUpdated(updatedAt);
    }
}
