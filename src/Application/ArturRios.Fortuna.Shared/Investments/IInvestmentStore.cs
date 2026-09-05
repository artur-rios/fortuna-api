using ArturRios.Fortuna.Domain.Investments;

namespace ArturRios.Fortuna.Shared.Investments;

public interface IInvestmentStore
{
    Task<InvestmentCreationResult> CreateAsync(
        InvestmentCreation creation,
        CancellationToken cancellationToken);
}

public interface IInvestmentReader
{
    IQueryable<InvestmentPositionSnapshot> QueryPositions();

    Task<InvestmentPositionSnapshot?> FindByIdWithPositionAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);

    IQueryable<InvestmentValuationReadSnapshot> QueryValuations(
        Guid userId,
        Guid investmentId);
}

public interface IInvestmentUpdater
{
    Task<InvestmentUpdateResult> UpdateAsync(
        InvestmentUpdate update,
        CancellationToken cancellationToken);
}

public interface IInvestmentLifecycleStore
{
    Task<InvestmentLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<InvestmentLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<InvestmentLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public enum InvestmentLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    RestoreRequiresSoftDeletion = 3,
    HardDeleteRequiresSoftDeletion = 4,
    HardDeleteHasLiveGoal = 5,
    DuplicateInstrument = 6
}

public sealed record InvestmentLifecycleResult(
    Guid? Id,
    InvestmentLifecycleOutcome Outcome,
    string? ReferencingGoal = null);

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

public sealed record InvestmentUpdate(
    Guid UserId,
    Guid Id,
    string Instrument,
    string? Institution,
    InvestmentType InvestmentType,
    DateTimeOffset UpdatedAt);

public sealed record InvestmentUpdateResult(
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

public sealed class InvestmentPositionSnapshot
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Instrument { get; init; } = string.Empty;
    public string? Institution { get; init; }
    public InvestmentType InvestmentType { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal Position { get; init; }
    public bool IsIndependentlyValued { get; init; }
    public decimal? LatestValuationValue { get; init; }
    public DateOnly? LatestValuationDate { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class InvestmentValuationReadSnapshot
{
    public Guid Id { get; init; }
    public Guid InvestmentId { get; init; }
    public decimal Value { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateOnly ValuedOn { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
