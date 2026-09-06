using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Ingestion;

public enum ConnectionStatus : short
{
    Active = 1,
    RequiresReauthentication = 2,
    Revoked = 3
}

public sealed class Connection
{
    private Connection()
    {
    }

    public Connection(
        UserProfile user,
        TransactionSourceType dataSourceType,
        string externalReference,
        byte[] accessTokenCipher,
        DateTimeOffset createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (dataSourceType is TransactionSourceType.Manual || !Enum.IsDefined(dataSourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(dataSourceType));
        }

        if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An external reference between 1 and 200 characters is required.",
                nameof(externalReference));
        }

        ArgumentNullException.ThrowIfNull(accessTokenCipher);
        if (accessTokenCipher.Length == 0)
        {
            throw new ArgumentException("An encrypted access token is required.", nameof(accessTokenCipher));
        }

        PublicId = Guid.NewGuid();
        UserId = user.Id;
        DataSourceType = dataSourceType;
        ExternalReference = externalReference.Trim();
        AccessTokenCipher = accessTokenCipher.ToArray();
        Status = ConnectionStatus.Active;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public TransactionSourceType DataSourceType { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public byte[] AccessTokenCipher { get; private set; } = [];
    public ConnectionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkRequiresReauthentication(DateTimeOffset updatedAt)
    {
        if (Status == ConnectionStatus.Revoked)
        {
            throw new InvalidOperationException("A revoked connection cannot be reauthenticated.");
        }

        Status = ConnectionStatus.RequiresReauthentication;
        UpdatedAt = updatedAt;
    }

    public void Reauthenticate(
        string externalReference,
        byte[] accessTokenCipher,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An external reference between 1 and 200 characters is required.",
                nameof(externalReference));
        }

        if (Status != ConnectionStatus.RequiresReauthentication)
        {
            throw new InvalidOperationException(
                "Only a connection requiring reauthentication can be reauthenticated.");
        }

        ArgumentNullException.ThrowIfNull(accessTokenCipher);
        if (accessTokenCipher.Length == 0)
        {
            throw new ArgumentException("An encrypted access token is required.", nameof(accessTokenCipher));
        }

        ExternalReference = externalReference.Trim();
        AccessTokenCipher = accessTokenCipher.ToArray();
        Status = ConnectionStatus.Active;
        UpdatedAt = updatedAt;
    }
}
