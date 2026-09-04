namespace ArturRios.Fortuna.Shared.Cards;

public interface ICreditCardStore
{
    Task<CreditCardCreationResult> CreateAsync(
        CreditCardCreation creation,
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
