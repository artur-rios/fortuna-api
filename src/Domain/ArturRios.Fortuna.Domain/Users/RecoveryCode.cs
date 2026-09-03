namespace ArturRios.Fortuna.Domain.Users;

/// <summary>A single-use local-account recovery code stored only as a one-way digest.</summary>
public sealed class RecoveryCode
{
    private RecoveryCode()
    {
    }

    internal RecoveryCode(LocalAccount localAccount, byte[] codeHash, DateTimeOffset createdAt)
    {
        if (codeHash.Length == 0)
        {
            throw new ArgumentException("A code hash is required.", nameof(codeHash));
        }

        LocalAccount = localAccount ?? throw new ArgumentNullException(nameof(localAccount));
        LocalAccountId = localAccount.Id;
        CodeHash = codeHash;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }
    public long LocalAccountId { get; private set; }
    public LocalAccount LocalAccount { get; private set; } = null!;
    public byte[] CodeHash { get; private set; } = [];
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkUsed(DateTimeOffset usedAt)
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException("The recovery code has already been used.");
        }

        UsedAt = usedAt;
    }
}
