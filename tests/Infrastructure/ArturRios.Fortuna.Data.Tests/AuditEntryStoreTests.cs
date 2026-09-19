using ArturRios.Fortuna.Data.Auditing;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class AuditEntryStoreTests
{
    [FunctionalFact]
    public async Task GivenUnsavedTrackedChanges_WhenEntryAppended_ThenOnlyTheEntryIsCommitted()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var data = await StoreTestData.SeedAsync(context);
        context.Categories.Add(new Category(data.User, "Left behind by a refusal", StoreTestData.Now));
        data.Category.UpdateDetails("Renamed but refused", null, StoreTestData.Now);

        await new EfAuditEntryStore(context).AppendAsync(
            new AuditEntryWrite(
                data.User.PublicId,
                true,
                "CreateCategoryCommand",
                null,
                null,
                AuditOutcome.Refused,
                "refused",
                StoreTestData.Now),
            CancellationToken.None);

        await using var verification = database.CreateContext();
        Assert.False(await verification.Categories.AnyAsync(category =>
            category.Name == "Left behind by a refusal"));
        Assert.Equal("General", (await verification.Categories.SingleAsync()).Name);
        var entry = await verification.AuditEntries.SingleAsync();
        Assert.Equal("CreateCategoryCommand", entry.Operation);
        Assert.Equal(AuditOutcome.Refused, entry.Outcome);
        Assert.NotNull(entry.SubjectReference);
    }
}
