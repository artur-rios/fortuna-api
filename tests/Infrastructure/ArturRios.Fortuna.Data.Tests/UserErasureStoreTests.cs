using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class UserErasureStoreTests
{
    [FunctionalFact]
    public async Task GivenOwnedIdentityRecordsAndObjects_WhenErased_ThenOnlyUnlinkedAuditRemains()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var objects = new RecordingObjectStore();
            Guid userId;
            long internalUserId;
            Guid otherUserId;
            Guid auditReference;
            await using (var context = CreateContext(path))
            {
                await context.Database.MigrateAsync();
                var seeded = await SeedAsync(context, objects, attachmentCount: 1);
                userId = seeded.UserId;
                internalUserId = seeded.InternalUserId;
                otherUserId = seeded.OtherUserId;
                auditReference = seeded.AuditReference;

                var result = await new EfUserErasureStore(context, objects).EraseAsync(
                    userId,
                    DateTimeOffset.Parse("2026-09-09T22:00:00Z"),
                    CancellationToken.None);

                Assert.NotNull(result);
                Assert.Equal(auditReference, result.SubjectReference);
                Assert.Equal(1, result.RevokedConnections);
                Assert.Equal(1, result.Erased["profiles"]);
                Assert.Equal(1, result.Erased["attachments"]);
                Assert.Equal(1, result.Erased["exports"]);
                Assert.Equal(1, result.Erased["imports"]);
                Assert.Equal(3, result.Erased["jobs"]);
            }

            await using var assertion = CreateContext(path);
            Assert.False(await assertion.UserProfiles.AnyAsync(item => item.PublicId == userId));
            Assert.True(await assertion.UserProfiles.AnyAsync(item => item.PublicId == otherUserId));
            Assert.False(await assertion.AuditSubjects.AnyAsync(
                item => item.SubjectReference == auditReference));
            Assert.Equal(3, await assertion.AuditEntries.CountAsync(
                item => item.SubjectReference == auditReference));
            var revocationAudit = await assertion.AuditEntries.SingleAsync(
                item => item.Operation == "RevokeConnectionsForUserErasure");
            Assert.Equal(auditReference, revocationAudit.SubjectReference);
            Assert.Null(revocationAudit.EntityType);
            Assert.Null(revocationAudit.EntityPublicId);
            Assert.Null(revocationAudit.Reason);
            var finalAudit = await assertion.AuditEntries.SingleAsync(
                item => item.Operation == "EraseUserCommand");
            Assert.Equal(auditReference, finalAudit.SubjectReference);
            Assert.Null(finalAudit.EntityType);
            Assert.Null(finalAudit.EntityPublicId);
            Assert.Null(finalAudit.Reason);
            Assert.Empty(objects.Keys);
            Assert.Empty(await assertion.BackgroundJobs.ToListAsync());
            Assert.False(await HasAnyOwnedRowAsync(assertion, internalUserId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [FunctionalFact]
    public async Task GivenObjectDeletionFailure_WhenErased_ThenDatabaseAndObjectsAreFullyRestored()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var objects = new RecordingObjectStore { FailDeleteAt = 2 };
            Guid userId;
            int rowCountBefore;
            await using (var context = CreateContext(path))
            {
                await context.Database.MigrateAsync();
                var seeded = await SeedAsync(context, objects, attachmentCount: 2);
                userId = seeded.UserId;
                rowCountBefore = await OwnedRowCountAsync(context, userId);

                await Assert.ThrowsAsync<IOException>(() =>
                    new EfUserErasureStore(context, objects).EraseAsync(
                        userId,
                        DateTimeOffset.Parse("2026-09-09T22:00:00Z"),
                        CancellationToken.None));
            }

            await using var assertion = CreateContext(path);
            Assert.True(await assertion.UserProfiles.AnyAsync(item => item.PublicId == userId));
            Assert.True(await assertion.AuditSubjects.AnyAsync(item => item.User.PublicId == userId));
            Assert.Equal(rowCountBefore, await OwnedRowCountAsync(assertion, userId));
            Assert.Equal(3, objects.Keys.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<SeedResult> SeedAsync(
        AppDbContext context,
        RecordingObjectStore objects,
        int attachmentCount)
    {
        var now = DateTimeOffset.Parse("2026-09-09T20:00:00Z");
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Erase Me", currency, now);
        var other = new UserProfile(Guid.NewGuid(), "Keep Me", currency, now);
        context.AddRange(currency, user, other);
        await context.SaveChangesAsync();

        var subject = new AuditSubject(user);
        var local = new LocalAccount(user, "Local", [1], [2], LocalAccountStorageMode.InMemory, now);
        local.AddRecoveryCode([3], now);
        var account = new FinancialAccount(
            user, "Checking", "Bank", FinancialAccountType.Checking, currency, 10m, now);
        var otherAccount = new FinancialAccount(
            other, "Other", null, FinancialAccountType.Cash, currency, 0m, now);
        var category = new Category(user, "Food", now);
        var connection = new Connection(
            user, TransactionSourceType.Pluggy, "item-1", [4], now);
        context.AddRange(subject, local, account, otherAccount, category, connection);
        await context.SaveChangesAsync();

        var transaction = new FinancialTransaction(
            user, account, category, TransactionDirection.Expense, 5m,
            new DateOnly(2026, 9, 9), now, "Personal purchase");
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        for (var index = 0; index < attachmentCount; index++)
        {
            var key = $"attachments/private-{index}.txt";
            context.Attachments.Add(new Attachment(
                transaction, $"private-{index}.txt", "text/plain", 7, key, now));
            await objects.PutAsync(key, "private");
        }

        var import = new ImportJob(user, connection, null, null, now);
        var importBackground = BackgroundJob.Create(
            "pluggy-synchronization",
            $"{{\"importJobId\":\"{import.PublicId}\",\"userId\":\"{user.PublicId}\"}}",
            $"pluggy-synchronization:{import.PublicId:N}",
            user.PublicId.ToString("D"),
            now);
        var export = new DataExport(
            user, DataExportFormat.Csv, "pt-BR", "private.csv", "{\"all\":true}",
            now, now.AddHours(1));
        var exportBackground = BackgroundJob.Create(
            "data-export", "{}", $"data-export:{export.PublicId:N}", user.PublicId.ToString("D"), now);
        var recurringBackground = BackgroundJob.Create(
            "recurring-transaction-materialization",
            $"{{\"userId\":\"{user.PublicId:D}\",\"through\":\"2026-09-30\"}}",
            $"recurring-transaction-materialization:{user.PublicId:N}:20260930",
            user.PublicId.ToString("D"),
            now);
        export.AttachBackgroundJob(exportBackground);
        export.Start(now.AddMinutes(1));
        export.Complete(1, "text/csv", "exports/private.csv", now.AddMinutes(2));
        await objects.PutAsync("exports/private.csv", "private-export");
        context.AddRange(import, importBackground, export, exportBackground, recurringBackground);
        context.AuditEntries.Add(new AuditEntry(
            subject.SubjectReference, "CreatePrivateRecord", "Transaction",
            transaction.PublicId, AuditOutcome.Succeeded, null, now));
        await context.SaveChangesAsync();

        return new SeedResult(user.PublicId, user.Id, other.PublicId, subject.SubjectReference);
    }

    private static async Task<bool> HasAnyOwnedRowAsync(AppDbContext context, long userId)
    {
        return await context.FinancialAccounts.AnyAsync(item => item.UserId == userId) ||
               await context.FinancialTransactions.AnyAsync(item => item.UserId == userId) ||
               await context.Categories.AnyAsync(item => item.UserId == userId) ||
               await context.Connections.AnyAsync(item => item.UserId == userId) ||
               await context.ImportJobs.AnyAsync(item => item.UserId == userId) ||
               await context.DataExports.AnyAsync(item => item.UserId == userId) ||
               await context.LocalAccounts.AnyAsync(item => item.UserId == userId);
    }

    private static async Task<int> OwnedRowCountAsync(AppDbContext context, Guid userId)
    {
        var id = await context.UserProfiles.Where(item => item.PublicId == userId)
            .Select(item => item.Id).SingleAsync();
        return await context.FinancialAccounts.CountAsync(item => item.UserId == id) +
               await context.FinancialTransactions.CountAsync(item => item.UserId == id) +
               await context.Categories.CountAsync(item => item.UserId == id) +
               await context.Connections.CountAsync(item => item.UserId == id) +
               await context.ImportJobs.CountAsync(item => item.UserId == id) +
               await context.DataExports.CountAsync(item => item.UserId == id) +
               await context.LocalAccounts.CountAsync(item => item.UserId == id);
    }

    private static AppDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);
        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"fortuna-erasure-{Guid.NewGuid():N}.db");

    private sealed record SeedResult(
        Guid UserId,
        long InternalUserId,
        Guid OtherUserId,
        Guid AuditReference);

    private sealed class RecordingObjectStore : IAttachmentStore
    {
        private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);
        private int deletes;

        public int? FailDeleteAt { get; init; }
        public IReadOnlyCollection<string> Keys => objects.Keys;

        public Task PutAsync(string key, string value)
        {
            objects[key] = System.Text.Encoding.UTF8.GetBytes(value);
            return Task.CompletedTask;
        }

        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            objects[key] = copy.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            if (!objects.TryGetValue(key, out var content))
            {
                throw new AttachmentObjectNotFoundException(key);
            }

            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            deletes++;
            if (deletes == FailDeleteAt)
            {
                throw new IOException("Injected object deletion failure.");
            }

            objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
