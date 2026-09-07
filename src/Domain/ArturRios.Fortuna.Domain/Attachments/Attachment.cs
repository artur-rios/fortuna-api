using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Domain.Attachments;

public sealed class Attachment : RecordLifecycleEntity
{
    private Attachment()
    {
    }

    public Attachment(
        FinancialTransaction transaction,
        string fileName,
        string contentType,
        long sizeInBytes,
        string storageKey,
        DateTimeOffset createdAt) : base(createdAt)
    {
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        FileName = Required(fileName, 300, nameof(fileName));
        ContentType = Required(contentType, 150, nameof(contentType));
        StorageKey = Required(storageKey, 500, nameof(storageKey));
        if (sizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
        }

        TransactionId = transaction.Id;
        SizeInBytes = sizeInBytes;
    }

    public long Id { get; private set; }
    public long TransactionId { get; private set; }
    public FinancialTransaction Transaction { get; private set; } = null!;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeInBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"A value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
