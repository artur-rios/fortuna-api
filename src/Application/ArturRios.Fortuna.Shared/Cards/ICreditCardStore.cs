namespace ArturRios.Fortuna.Shared.Cards;

public interface ICreditCardStore
{
    Task<CreditCardCreationResult> CreateAsync(
        CreditCardCreation creation,
        CancellationToken cancellationToken);
}

public interface ICreditCardReader
{
    IQueryable<CreditCardLimitSnapshot> QueryLimits();

    Task<CreditCardLimitSnapshot?> FindByIdWithLimitsAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public interface ICreditCardUpdater
{
    Task<CreditCardUpdateResult> UpdateAsync(
        CreditCardUpdate update,
        CancellationToken cancellationToken);
}

public sealed record CreditCardCreation(
    Guid UserId,
    string Name,
    string Issuer,
    string CurrencyCode,
    decimal CreditLimit,
    short ClosingDay,
    short DueDay,
    string? LastFourDigits,
    DateTimeOffset CreatedAt);

public sealed record CreditCardCreationResult(
    CreditCardSnapshot? Card,
    bool DuplicateName);

public sealed record CreditCardUpdate(
    Guid UserId,
    Guid Id,
    string Name,
    string Issuer,
    decimal CreditLimit,
    short ClosingDay,
    short DueDay,
    DateTimeOffset UpdatedAt);

public sealed record CreditCardUpdateResult(
    CreditCardSnapshot? Card,
    bool DuplicateName);

public sealed record CreditCardSnapshot(
    Guid Id,
    Guid UserId,
    string Name,
    string Issuer,
    string CurrencyCode,
    decimal CreditLimit,
    short ClosingDay,
    short DueDay,
    string? LastFourDigits,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreditCardLimitSnapshot
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public short ClosingDay { get; init; }
    public short DueDay { get; init; }
    public string? LastFourDigits { get; init; }
    public decimal OutstandingAmount { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
