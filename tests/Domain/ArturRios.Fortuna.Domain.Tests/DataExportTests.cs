using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class DataExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidRequest_WhenExportCreated_ThenItIsPendingAndOwnerBound()
    {
        var user = User();

        var export = Export(user);

        Assert.NotEqual(Guid.Empty, export.PublicId);
        Assert.Equal(user, export.User);
        Assert.Equal(DataExportStatus.Pending, export.Status);
        Assert.Equal(DataExportFormat.Csv, export.Format);
        Assert.Equal(Now.AddHours(24), export.ExpiresAt);
    }

    [UnitFact]
    public void GivenPendingExport_WhenProcessed_ThenFileMetadataIsRecorded()
    {
        var export = Export(User());
        var job = BackgroundJob.Create("data-export", "{}", "export-1", null, Now);
        export.AttachBackgroundJob(job);

        export.Start(Now.AddMinutes(1));
        export.Complete(3, "text/csv", "exports/result.csv", Now.AddMinutes(2));

        Assert.Equal(job.Id, export.BackgroundJobId);
        Assert.Equal(DataExportStatus.Completed, export.Status);
        Assert.Equal(3, export.RowCount);
        Assert.Equal("exports/result.csv", export.StorageKey);
    }

    [UnitFact]
    public void GivenInvalidTransition_WhenExportCompletedBeforeStart_ThenItIsRejected()
    {
        var export = Export(User());

        Assert.Throws<InvalidOperationException>(() => export.Complete(
            0, "text/csv", "exports/result.csv", Now.AddMinutes(1)));
    }

    [UnitFact]
    public void GivenPersonalArchive_WhenCreatedAsZip_ThenItsKindIsExplicit()
    {
        var export = new DataExport(
            User(),
            DataExportFormat.Zip,
            "und",
            "personal.zip",
            "{}",
            Now,
            Now.AddHours(24),
            DataExportKind.PersonalArchive);

        Assert.Equal(DataExportKind.PersonalArchive, export.Kind);
        Assert.Equal(DataExportFormat.Zip, export.Format);
    }

    [UnitTheory]
    [InlineData(DataExportFormat.Zip, DataExportKind.DataSet)]
    [InlineData(DataExportFormat.Csv, DataExportKind.PersonalArchive)]
    public void GivenIncompatibleKindAndFormat_WhenCreated_ThenItIsRejected(
        DataExportFormat format,
        DataExportKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataExport(
            User(),
            format,
            "und",
            "export.file",
            "{}",
            Now,
            Now.AddHours(24),
            kind));
    }

    private static DataExport Export(UserProfile user) => new(
        user,
        DataExportFormat.Csv,
        "pt-BR",
        "transactions.csv",
        "{}",
        Now,
        Now.AddHours(24));

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian Real", 2),
        Now);
}
