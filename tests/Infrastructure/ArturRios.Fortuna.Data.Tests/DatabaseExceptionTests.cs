using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class DatabaseExceptionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T12:00:00Z");

    [FunctionalFact]
    public async Task GivenSqliteDuplicateLiveTag_WhenUniqueViolationIsInspected_ThenOnlyItsIndexMatches()
    {
        var path = SqliteTestDatabase.TemporaryPath();
        try
        {
            await using var context = SqliteTestDatabase.CreateContext(path);
            await context.Database.MigrateAsync();
            var currency = new Currency("BRL", "Brazilian Real", 2);
            var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
            context.AddRange(currency, user, new Tag(user, "Travel", Now));
            await context.SaveChangesAsync();
            context.Tags.Add(new Tag(user, " travel ", Now));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                context.SaveChangesAsync());

            Assert.True(DatabaseException.IsUniqueViolation(exception));
            Assert.True(DatabaseException.IsUniqueViolation(exception, TagMap.LiveNameIndex));
            Assert.True(DatabaseException.IsUniqueViolation(
                exception,
                CounterpartyMap.LiveNameIndex,
                TagMap.LiveNameIndex));
            Assert.False(DatabaseException.IsUniqueViolation(exception, CounterpartyMap.LiveNameIndex));
            Assert.False(DatabaseException.IsUniqueViolation(exception, LocalAccountMap.NameIndex));
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }
}
