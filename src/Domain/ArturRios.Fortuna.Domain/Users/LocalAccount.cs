namespace ArturRios.Fortuna.Domain.Users;

/// <summary>The single offline identity owned by a desktop Fortuna installation.</summary>
public sealed class LocalAccount
{
    private LocalAccount()
    {
    }

    public LocalAccount(
        UserProfile user,
        string name,
        byte[] secretHash,
        byte[] salt,
        LocalAccountStorageMode storageMode,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("A name between 1 and 200 characters is required.", nameof(name));
        }

        if (secretHash.Length == 0)
        {
            throw new ArgumentException("A secret hash is required.", nameof(secretHash));
        }

        if (salt.Length == 0)
        {
            throw new ArgumentException("A salt is required.", nameof(salt));
        }

        if (!Enum.IsDefined(storageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageMode));
        }

        PublicId = Guid.NewGuid();
        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Name = name;
        SecretHash = secretHash;
        Salt = salt;
        StorageMode = storageMode;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public byte[] SecretHash { get; private set; } = [];
    public byte[] Salt { get; private set; } = [];
    public LocalAccountStorageMode StorageMode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<RecoveryCode> RecoveryCodes { get; private set; } = [];

    public void AddRecoveryCode(byte[] codeHash, DateTimeOffset createdAt) =>
        RecoveryCodes.Add(new RecoveryCode(this, codeHash, createdAt));

    public void ReplaceSecret(byte[] secretHash, byte[] salt, DateTimeOffset updatedAt)
    {
        if (secretHash.Length == 0)
        {
            throw new ArgumentException("A secret hash is required.", nameof(secretHash));
        }

        if (salt.Length == 0)
        {
            throw new ArgumentException("A salt is required.", nameof(salt));
        }

        SecretHash = secretHash;
        Salt = salt;
        UpdatedAt = updatedAt;
    }
}
