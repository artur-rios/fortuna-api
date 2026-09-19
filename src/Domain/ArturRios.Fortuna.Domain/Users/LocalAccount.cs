using ArturRios.Fortuna.Domain.Guards;

namespace ArturRios.Fortuna.Domain.Users;

/// <summary>The single offline identity owned by a desktop Fortuna installation.</summary>
public sealed class LocalAccount
{
    private readonly List<RecoveryCode> _recoveryCodes = [];

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
        name = BoundedText.Required(
            name,
            200,
            nameof(name),
            "A name between 1 and 200 characters is required.");
        EnsureSecret(secretHash, salt);
        if (!Enum.IsDefined(storageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageMode));
        }

        PublicId = Guid.NewGuid();
        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Name = name;
        SecretHash = secretHash.ToArray();
        Salt = salt.ToArray();
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
    public IReadOnlyCollection<RecoveryCode> RecoveryCodes => _recoveryCodes;

    public void AddRecoveryCode(byte[] codeHash, DateTimeOffset createdAt) =>
        _recoveryCodes.Add(new RecoveryCode(this, codeHash, createdAt));

    public void ReplaceSecret(byte[] secretHash, byte[] salt, DateTimeOffset updatedAt)
    {
        EnsureSecret(secretHash, salt);
        SecretHash = secretHash.ToArray();
        Salt = salt.ToArray();
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Replaces every recovery code at once. All replacements are built (and so validated)
    /// before the current set is dropped, so a bad hash cannot leave a partial set behind.
    /// </summary>
    public void ReplaceRecoveryCodes(
        IEnumerable<byte[]> recoveryCodeHashes,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodeHashes);
        var replacements = recoveryCodeHashes
            .Select(codeHash => new RecoveryCode(this, codeHash, updatedAt))
            .ToArray();
        _recoveryCodes.Clear();
        _recoveryCodes.AddRange(replacements);
        UpdatedAt = updatedAt;
    }

    private static void EnsureSecret(byte[] secretHash, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(secretHash);
        ArgumentNullException.ThrowIfNull(salt);
        if (secretHash.Length == 0)
        {
            throw new ArgumentException("A secret hash is required.", nameof(secretHash));
        }

        if (salt.Length == 0)
        {
            throw new ArgumentException("A salt is required.", nameof(salt));
        }
    }
}
