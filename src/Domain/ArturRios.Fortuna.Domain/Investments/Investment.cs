using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Investments;

public enum InvestmentType : short
{
    FixedIncome = 1,
    Equity = 2,
    Fund = 3,
    Other = 4
}

public sealed class Investment : RecordLifecycleEntity
{
    private Investment()
    {
    }

    public Investment(
        UserProfile user,
        string instrument,
        string? institution,
        InvestmentType investmentType,
        Currency currency,
        DateTimeOffset createdAt) : base(createdAt)
    {
        if (string.IsNullOrWhiteSpace(instrument) || instrument.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An instrument name between 1 and 200 characters is required.",
                nameof(instrument));
        }

        if (institution?.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An institution cannot exceed 200 characters.",
                nameof(institution));
        }

        if (!Enum.IsDefined(investmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(investmentType));
        }

        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Instrument = instrument.Trim();
        NormalizedInstrument = Instrument.ToUpperInvariant();
        Institution = string.IsNullOrWhiteSpace(institution) ? null : institution.Trim();
        InvestmentType = investmentType;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        CurrencyId = currency.Id;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Instrument { get; private set; } = string.Empty;
    public string NormalizedInstrument { get; private set; } = string.Empty;
    public string? Institution { get; private set; }
    public InvestmentType InvestmentType { get; private set; }
    public long CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;

    public void UpdateDetails(
        string instrument,
        string? institution,
        InvestmentType investmentType,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(instrument) || instrument.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An instrument name between 1 and 200 characters is required.",
                nameof(instrument));
        }

        if (institution?.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An institution cannot exceed 200 characters.",
                nameof(institution));
        }

        if (!Enum.IsDefined(investmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(investmentType));
        }

        Instrument = instrument.Trim();
        NormalizedInstrument = Instrument.ToUpperInvariant();
        Institution = string.IsNullOrWhiteSpace(institution) ? null : institution.Trim();
        InvestmentType = investmentType;
        MarkUpdated(updatedAt);
    }
}
