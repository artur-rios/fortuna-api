namespace ArturRios.Fortuna.Shared.Projections;

public interface ICommittedObligationReader
{
    Task<IReadOnlyCollection<CommittedObligationSnapshot>> ReadAsync(
        Guid userId,
        DateOnly asOf,
        DateOnly through,
        CancellationToken cancellationToken);
}

public enum CommittedObligationKind
{
    Installment = 1,
    Statement = 2
}

public sealed record CommittedObligationSnapshot(
    Guid Id,
    CommittedObligationKind Kind,
    DateOnly DueDate,
    DateOnly? CycleStart,
    DateOnly? CycleEnd,
    string CurrencyCode,
    decimal Amount);
