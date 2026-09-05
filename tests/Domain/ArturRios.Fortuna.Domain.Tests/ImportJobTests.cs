using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class ImportJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenExternalSource_WhenJobCreated_ThenItStartsPendingForOwner()
    {
        var user = User();

        var job = new ImportJob(user, TransactionSourceType.Excel, Now);

        Assert.NotEqual(Guid.Empty, job.PublicId);
        Assert.Equal(user, job.User);
        Assert.Equal(TransactionSourceType.Excel, job.SourceType);
        Assert.Equal(ImportJobStatus.Pending, job.Status);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Equal(Now, job.UpdatedAt);
    }

    [UnitTheory]
    [InlineData(TransactionSourceType.Manual)]
    [InlineData((TransactionSourceType)99)]
    public void GivenInvalidSource_WhenJobCreated_ThenItIsRejected(
        TransactionSourceType sourceType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ImportJob(User(), sourceType, Now));
    }

    [UnitFact]
    public void GivenValidRawRecord_WhenCreated_ThenPayloadAndNormalizedFiguresAreRetained()
    {
        const string payload = "{\"amount\":10.25,\"description\":\"Lunch\"}";
        var job = new ImportJob(User(), TransactionSourceType.Pdf, Now);
        var occurredOn = new DateOnly(2026, 9, 4);

        var record = new ImportedRecord(
            job,
            payload,
            ImportedRecordOutcome.Imported,
            10.25m,
            occurredOn,
            "  source-1  ");

        Assert.Equal(job, record.ImportJob);
        Assert.Equal(payload, record.RawPayload);
        Assert.Equal("source-1", record.ExternalId);
        Assert.Equal(10.25m, record.Amount);
        Assert.Equal(occurredOn, record.OccurredOn);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("not json")]
    public void GivenInvalidPayload_WhenRecordCreated_ThenItIsRejected(string payload)
    {
        var job = new ImportJob(User(), TransactionSourceType.Excel, Now);

        Assert.ThrowsAny<Exception>(() => new ImportedRecord(
            job,
            payload,
            ImportedRecordOutcome.Imported,
            10m,
            new DateOnly(2026, 9, 4)));
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveNormalizedAmount_WhenRecordCreated_ThenItIsRejected(decimal amount)
    {
        var job = new ImportJob(User(), TransactionSourceType.Excel, Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedRecord(
            job,
            "{}",
            ImportedRecordOutcome.Imported,
            amount,
            new DateOnly(2026, 9, 4)));
    }

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian real", 2),
        Now);
}
