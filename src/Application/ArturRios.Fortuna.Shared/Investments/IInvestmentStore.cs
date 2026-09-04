using ArturRios.Fortuna.Domain.Investments;

namespace ArturRios.Fortuna.Shared.Investments;

public interface IInvestmentStore
{
    Task<InvestmentCreationResult> CreateAsync(
        InvestmentCreation creation,
        CancellationToken cancellationToken);
}

public sealed record InvestmentCreation(
    Guid UserId,
    string Instrument,
    string? Institution,
    InvestmentType InvestmentType,
    string CurrencyCode,
    DateTimeOffset CreatedAt);

public sealed record InvestmentCreationResult(
    InvestmentSnapshot? Investment,
    bool DuplicateInstrument);

public sealed record InvestmentSnapshot(
    Guid Id,
    Guid UserId,
    string Instrument,
    string? Institution,
    InvestmentType InvestmentType,
    string CurrencyCode,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
