using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Exports;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class PersonalDataArchiveTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-10T01:00:00Z");

    [FunctionalFact]
    public async Task GivenOwnedData_WhenArchiveBuilt_ThenManifestSchemasExactMoneyAndAttachmentArePortable()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var objects = new MemoryObjectStore();
            Guid userId;
            await using (var context = CreateContext(path))
            {
                await context.Database.MigrateAsync();
                userId = await SeedAsync(context, objects);
                var result = await new EfPersonalDataArchiveBuilder(context, objects).BuildAsync(
                    userId,
                    Now,
                    Now.AddHours(24),
                    CancellationToken.None);

                Assert.Equal(PersonalDataArchiveCoverage.Included.Count, result.Parts.Count);
                using var archive = new ZipArchive(
                    new MemoryStream(result.Content, writable: false),
                    ZipArchiveMode.Read);
                var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
                Assert.Contains("manifest.json", names);
                foreach (var part in PersonalDataArchiveCoverage.Included)
                {
                    Assert.Contains($"data/{part.Name}.json", names);
                    Assert.Contains($"schemas/{part.Name}.schema.json", names);
                }

                var manifest = JsonDocument.Parse(await ReadAsync(archive, "manifest.json"));
                Assert.Equal("fortuna-personal-data",
                    manifest.RootElement.GetProperty("archiveType").GetString());
                Assert.Equal(PersonalDataArchiveCoverage.Included.Count,
                    manifest.RootElement.GetProperty("parts").GetArrayLength());
                var accounts = await ReadAsync(archive, "data/financial-accounts.json");
                Assert.Contains("123456789012345.6789", accounts, StringComparison.Ordinal);
                var transactions = await ReadAsync(archive, "data/transactions.json");
                Assert.Contains("0.0001", transactions, StringComparison.Ordinal);
                var consents = await ReadAsync(archive, "data/processing-consents.json");
                Assert.Contains("externalDataProcessing", consents, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("2026-09", consents, StringComparison.Ordinal);
                var attachment = archive.Entries.Single(entry =>
                    entry.FullName.StartsWith("attachments/", StringComparison.Ordinal));
                Assert.Equal("portable attachment", await ReadAsync(attachment));

                var json = string.Join('\n', await Task.WhenAll(archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".json", StringComparison.Ordinal))
                    .Select(ReadAsync)));
                Assert.DoesNotContain("PASSWORD-HASH-SECRET", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("RECOVERY-CODE-SECRET", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CONNECTION-TOKEN-SECRET", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IMPORTED-ACCESS-TOKEN", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IMPORTED-PASSWORD", json, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("[redacted]", json, StringComparison.Ordinal);
                Assert.DoesNotContain("accessTokenCipher", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secretHash", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("codeHash", json, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [FunctionalFact]
    public async Task GivenProfileOnly_WhenArchiveBuilt_ThenEveryPartExistsAndNonProfilePartsAreEmpty()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var objects = new MemoryObjectStore();
            await using var context = CreateContext(path);
            await context.Database.MigrateAsync();
            var currency = new Currency("BRL", "Brazilian Real", 4);
            var user = new UserProfile(Guid.NewGuid(), "Profile Only", currency, Now);
            context.AddRange(currency, user);
            await context.SaveChangesAsync();

            var result = await new EfPersonalDataArchiveBuilder(context, objects).BuildAsync(
                user.PublicId,
                Now,
                Now.AddHours(24),
                CancellationToken.None);

            using var archive = new ZipArchive(
                new MemoryStream(result.Content, writable: false),
                ZipArchiveMode.Read);
            foreach (var part in PersonalDataArchiveCoverage.Included)
            {
                var document = JsonDocument.Parse(await ReadAsync(
                    archive,
                    $"data/{part.Name}.json"));
                Assert.Equal(part == PersonalDataArchiveCoverage.Profile ? 1 : 0,
                    document.RootElement.GetProperty("records").GetArrayLength());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [UnitFact]
    public void GivenMappedOwnerGraph_WhenCoverageCompared_ThenEveryOwnedEntityHasAPolicy()
    {
        using var context = CreateContext(TemporaryDatabasePath());
        var root = context.Model.FindEntityType(typeof(UserProfile))!;
        var owned = new HashSet<Type> { typeof(UserProfile) };
        var pending = new Queue<Microsoft.EntityFrameworkCore.Metadata.IEntityType>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var principal))
        {
            foreach (var foreignKey in principal.GetReferencingForeignKeys())
            {
                var dependent = foreignKey.DeclaringEntityType;
                if (dependent.ClrType == typeof(Dictionary<string, object>) ||
                    !owned.Add(dependent.ClrType))
                {
                    continue;
                }
                pending.Enqueue(dependent);
            }
        }

        var policies = PersonalDataArchiveCoverage.Included
            .Select(part => part.EntityType)
            .Concat(PersonalDataArchiveCoverage.SecretOrOperationalExclusions)
            .ToHashSet();
        var missing = owned.Except(policies).Select(type => type.Name).Order().ToArray();
        Assert.Empty(missing);
        Assert.Contains(PersonalDataArchiveCoverage.AuditEntries,
            PersonalDataArchiveCoverage.Included);
    }

    private static async Task<Guid> SeedAsync(
        AppDbContext context,
        MemoryObjectStore objects)
    {
        var currency = new Currency("BRL", "Brazilian Real", 4);
        var user = new UserProfile(Guid.NewGuid(), "Portable User", currency, Now);
        context.AddRange(currency, user);
        await context.SaveChangesAsync();

        var subject = new AuditSubject(user);
        var local = new LocalAccount(
            user,
            "Portable Local",
            Encoding.UTF8.GetBytes("PASSWORD-HASH-SECRET"),
            Encoding.UTF8.GetBytes("salt"),
            LocalAccountStorageMode.InMemory,
            Now);
        local.AddRecoveryCode(Encoding.UTF8.GetBytes("RECOVERY-CODE-SECRET"), Now);
        var account = new FinancialAccount(
            user,
            "Exact Account",
            "Portable Bank",
            FinancialAccountType.Checking,
            currency,
            123456789012345.6789m,
            Now);
        var category = new Category(user, "Portable", Now);
        var connection = new Connection(
            user,
            TransactionSourceType.Pluggy,
            "portable-connection",
            Encoding.UTF8.GetBytes("CONNECTION-TOKEN-SECRET"),
            Now);
        var consent = new ProcessingConsent(
            user, ProcessingConsentPurpose.ExternalDataProcessing, "2026-09", Now);
        context.AddRange(subject, local, account, category, connection, consent);
        await context.SaveChangesAsync();

        var importJob = new ImportJob(user, TransactionSourceType.Excel, Now);
        var importedRecord = new ImportedRecord(
            importJob,
            "{\"access_token\":\"IMPORTED-ACCESS-TOKEN\",\"nested\":{\"password\":\"IMPORTED-PASSWORD\"},\"description\":\"portable\"}",
            ImportedRecordOutcome.Imported,
            0.0001m,
            new DateOnly(2026, 9, 10));
        context.AddRange(importJob, importedRecord);
        await context.SaveChangesAsync();

        var transaction = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            0.0001m,
            new DateOnly(2026, 9, 10),
            Now,
            "Portable transaction");
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        const string storageKey = "attachments/portable.txt";
        context.Attachments.Add(new Attachment(
            transaction,
            "portable.txt",
            "text/plain",
            19,
            storageKey,
            Now));
        context.AuditEntries.Add(new AuditEntry(
            subject.SubjectReference,
            "CreatePortableRecord",
            "Transaction",
            transaction.PublicId,
            AuditOutcome.Succeeded,
            null,
            Now));
        await context.SaveChangesAsync();
        await objects.PutAsync(storageKey, "portable attachment");
        return user.PublicId;
    }

    private static async Task<string> ReadAsync(ZipArchive archive, string path) =>
        await ReadAsync(archive.GetEntry(path) ?? throw new InvalidOperationException(path));

    private static async Task<string> ReadAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
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
        Path.Combine(Path.GetTempPath(), $"fortuna-portability-{Guid.NewGuid():N}.db");

    private sealed class MemoryObjectStore : IAttachmentStore
    {
        private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);

        public Task PutAsync(string key, string value)
        {
            objects[key] = Encoding.UTF8.GetBytes(value);
            return Task.CompletedTask;
        }

        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            objects[key] = copy.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(objects[key], writable: false));

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
