namespace ArturRios.Fortuna.Shared.Users;

public interface IUserErasureStore
{
    Task<UserErasureResult?> EraseAsync(
        Guid userId,
        DateTimeOffset erasedAt,
        CancellationToken cancellationToken);
}

public sealed record UserErasureResult(
    Guid SubjectReference,
    IReadOnlyDictionary<string, int> Erased,
    int RevokedConnections);
