namespace ArturRios.Fortuna.Shared.Classification;

public interface ICategoryStore
{
    Task<CategoryCreationResult> CreateAsync(
        CategoryCreation creation,
        CancellationToken cancellationToken);
}

public interface ICategoryReader
{
    Task<IReadOnlyCollection<CategoryReadSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        bool includeUsageCounts,
        CancellationToken cancellationToken);
}

public enum CategoryCreationOutcome
{
    Succeeded = 1,
    ParentNotFound = 2,
    DuplicateSiblingName = 3,
    CycleDetected = 4
}

public sealed record CategoryCreation(
    Guid UserId,
    string Name,
    Guid? ParentId,
    DateTimeOffset CreatedAt);

public sealed record CategoryCreationResult(
    CategorySnapshot? Category,
    CategoryCreationOutcome Outcome);

public sealed record CategorySnapshot(
    Guid Id,
    Guid UserId,
    string Name,
    Guid? ParentId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CategoryReadSnapshot(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool IsDeleted,
    int DirectUsageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
