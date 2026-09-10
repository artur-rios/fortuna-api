using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Exports;

public enum DataExportFormat : short
{
    Csv = 1,
    Excel = 2,
    Pdf = 3,
    Zip = 4
}

public enum DataExportKind : short
{
    DataSet = 1,
    PersonalArchive = 2
}

public enum DataExportStatus : short
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}

public sealed class DataExport
{
    private DataExport()
    {
    }

    public DataExport(
        UserProfile user,
        DataExportFormat format,
        string locale,
        string fileName,
        string requestJson,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DataExportKind kind = DataExportKind.DataSet)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (!Enum.IsDefined(kind) ||
            (kind == DataExportKind.DataSet && format == DataExportFormat.Zip) ||
            (kind == DataExportKind.PersonalArchive && format != DataExportFormat.Zip))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        PublicId = Guid.NewGuid();
        UserId = user.Id;
        Format = format;
        Kind = kind;
        Locale = Required(locale, 35, nameof(locale));
        FileName = Required(fileName, 300, nameof(fileName));
        RequestJson = string.IsNullOrWhiteSpace(requestJson)
            ? throw new ArgumentException("An export request is required.", nameof(requestJson))
            : requestJson;
        Status = DataExportStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public Guid? BackgroundJobId { get; private set; }
    public BackgroundJob? BackgroundJob { get; private set; }
    public DataExportFormat Format { get; private set; }
    public DataExportKind Kind { get; private set; }
    public DataExportStatus Status { get; private set; }
    public string Locale { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string RequestJson { get; private set; } = string.Empty;
    public int? RowCount { get; private set; }
    public string? ContentType { get; private set; }
    public string? StorageKey { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public void AttachBackgroundJob(BackgroundJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (BackgroundJobId.HasValue)
        {
            throw new InvalidOperationException("The export already has a background job.");
        }

        BackgroundJob = job;
        BackgroundJobId = job.Id;
    }

    public void Start(DateTimeOffset updatedAt)
    {
        if (Status != DataExportStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending export can start.");
        }

        Status = DataExportStatus.Running;
        FailureReason = null;
        UpdatedAt = updatedAt;
    }

    public void Complete(
        int rowCount,
        string contentType,
        string storageKey,
        DateTimeOffset updatedAt)
    {
        if (Status != DataExportStatus.Running)
        {
            throw new InvalidOperationException("Only a running export can complete.");
        }

        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        RowCount = rowCount;
        ContentType = Required(contentType, 150, nameof(contentType));
        StorageKey = Required(storageKey, 500, nameof(storageKey));
        FailureReason = null;
        Status = DataExportStatus.Completed;
        UpdatedAt = updatedAt;
    }

    public void Fail(string reason, DateTimeOffset updatedAt)
    {
        if (Status is not (DataExportStatus.Pending or DataExportStatus.Running))
        {
            throw new InvalidOperationException("Only an unfinished export can fail.");
        }

        FailureReason = Required(reason, 1000, nameof(reason));
        Status = DataExportStatus.Failed;
        UpdatedAt = updatedAt;
    }

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
                $"A value cannot exceed {maximumLength} characters.", parameterName);
    }
}
