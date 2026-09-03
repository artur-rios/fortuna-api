using System.Text;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class LocalAccountTests
{
    private static readonly Currency Currency = new("BRL", "Brazilian Real", 2);

    [UnitFact]
    public void GivenValidLocalIdentity_WhenAccountIsCreated_ThenProfileHasNoExternalSubject()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-03T00:00:00Z");
        var user = new UserProfile("Local User", Currency, createdAt);

        var account = new LocalAccount(
            user,
            "Local User",
            [1, 2, 3],
            [4, 5, 6],
            LocalAccountStorageMode.InMemory,
            createdAt);
        account.AddRecoveryCode([7, 8, 9], createdAt);

        Assert.Null(user.ExternalSubject);
        Assert.Equal("Local User", account.Name);
        Assert.Equal(LocalAccountStorageMode.InMemory, account.StorageMode);
        Assert.Single(account.RecoveryCodes);
        Assert.Equal([7, 8, 9], account.RecoveryCodes.Single().CodeHash);
        Assert.DoesNotContain(
            account.GetType().GetProperties(),
            property => property.Name.Contains("Secret", StringComparison.Ordinal) &&
                        property.PropertyType == typeof(string));
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenBlankName_WhenAccountIsCreated_ThenCreationIsRejected(string name)
    {
        var user = new UserProfile("Local User", Currency, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => new LocalAccount(
            user,
            name,
            Encoding.UTF8.GetBytes("hash"),
            Encoding.UTF8.GetBytes("salt"),
            LocalAccountStorageMode.InMemory,
            DateTimeOffset.UtcNow));
    }
}
