using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class ReadPathTests
{
    [FunctionalFact]
    public async Task GivenCategoryTree_WhenSubtreeIsRead_ThenOnlyTheCategoryAndItsVisibleDescendantsReturn()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var living = new Category(data.User, "Living", StoreTestData.Now);
            var dining = new Category(data.User, "Dining", StoreTestData.Now, living);
            var restaurants = new Category(data.User, "Restaurants", StoreTestData.Now, dining);
            var groceries = new Category(data.User, "Groceries", StoreTestData.Now, living);
            var unrelated = new Category(data.User, "Travel", StoreTestData.Now);
            context.AddRange(living, dining, restaurants, groceries, unrelated);
            await context.SaveChangesAsync();
            groceries.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = CategoryStore(context);

            var visible = await store.ListSubtreeAsync(
                data.User.PublicId, dining.PublicId, false, true, CancellationToken.None);
            var withDeleted = await store.ListSubtreeAsync(
                data.User.PublicId, living.PublicId, true, false, CancellationToken.None);
            var foreign = await store.ListSubtreeAsync(
                Guid.NewGuid(), living.PublicId, true, false, CancellationToken.None);

            Assert.Equal(
                ["Dining", "Restaurants"],
                visible.Select(item => item.Name).Order());
            Assert.Equal(
                ["Dining", "Groceries", "Living", "Restaurants"],
                withDeleted.Select(item => item.Name).Order());
            Assert.Empty(foreign);
        });
    }

    [FunctionalFact]
    public async Task GivenManyTags_WhenPaged_ThenThePageAndTotalAreReturned()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            context.Tags.AddRange(
                new Tag(data.User, "Alpha", StoreTestData.Now),
                new Tag(data.User, "Bravo", StoreTestData.Now),
                new Tag(data.User, "Charlie", StoreTestData.Now));
            await context.SaveChangesAsync();
            var store = new EfTagStore(context, new TagOptions(10));

            var page = await store.ListAsync(
                data.User.PublicId, false, new PageRequest(2, 2), CancellationToken.None);

            Assert.Equal(3, page.TotalItems);
            Assert.Equal("Charlie", Assert.Single(page.Items).Name);
        });
    }

    [FunctionalFact]
    public async Task GivenInvestments_WhenExistenceIsChecked_ThenOnlyOwnedLiveOnesExist()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var live = new Investment(
                data.User, "Treasury", null, InvestmentType.FixedIncome, data.Currency,
                StoreTestData.Now);
            var deleted = new Investment(
                data.User, "Fund", null, InvestmentType.Fund, data.Currency, StoreTestData.Now);
            context.AddRange(live, deleted);
            await context.SaveChangesAsync();
            deleted.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = new EfInvestmentStore(
                context,
                new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

            Assert.True(await store.ExistsAsync(
                data.User.PublicId, live.PublicId, CancellationToken.None));
            Assert.False(await store.ExistsAsync(
                data.User.PublicId, deleted.PublicId, CancellationToken.None));
            Assert.False(await store.ExistsAsync(
                Guid.NewGuid(), live.PublicId, CancellationToken.None));
        });
    }

    private static EfCategoryStore CategoryStore(AppDbContext context) => new(
        context,
        new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-read-path");
        try
        {
            await using var context = SqliteTestDatabase.CreateContext(path);
            await context.Database.MigrateAsync();
            await test(context);
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }
}
