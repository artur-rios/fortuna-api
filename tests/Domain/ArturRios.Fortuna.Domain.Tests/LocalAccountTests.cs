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

    [UnitFact]
    public void GivenNewSecretHash_WhenSecretIsReplaced_ThenCredentialsAndTimestampChange()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-03T00:00:00Z");
        var updatedAt = createdAt.AddHours(1);
        var account = Account(createdAt);

        account.ReplaceSecret([9, 8, 7], [6, 5, 4], updatedAt);

        Assert.Equal([9, 8, 7], account.SecretHash);
        Assert.Equal([6, 5, 4], account.Salt);
        Assert.Equal(updatedAt, account.UpdatedAt);
    }

    [UnitFact]
    public void GivenUnusedRecoveryCode_WhenMarkedUsed_ThenConsumptionIsPermanent()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-03T00:00:00Z");
        var usedAt = createdAt.AddHours(1);
        var account = Account(createdAt);
        account.AddRecoveryCode([7, 8, 9], createdAt);
        var code = account.RecoveryCodes.Single();

        code.MarkUsed(usedAt);

        Assert.Equal(usedAt, code.UsedAt);
        Assert.Throws<InvalidOperationException>(() => code.MarkUsed(usedAt.AddMinutes(1)));
        Assert.Equal(usedAt, code.UsedAt);
    }

    [UnitFact]
    public void GivenReplacementRecoveryCodes_WhenReplacingSet_ThenEveryOldCodeIsInvalidated()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-03T00:00:00Z");
        var updatedAt = createdAt.AddHours(1);
        var account = Account(createdAt);
        account.AddRecoveryCode([1, 1, 1], createdAt);
        account.AddRecoveryCode([2, 2, 2], createdAt);
        account.RecoveryCodes.First().MarkUsed(createdAt.AddMinutes(1));

        account.ReplaceRecoveryCodes([[3, 3, 3], [4, 4, 4]], updatedAt);

        Assert.Equal(2, account.RecoveryCodes.Count);
        Assert.Equal([[3, 3, 3], [4, 4, 4]], account.RecoveryCodes.Select(code => code.CodeHash));
        Assert.All(account.RecoveryCodes, code => Assert.Null(code.UsedAt));
        Assert.Equal(updatedAt, account.UpdatedAt);
    }

    private static LocalAccount Account(DateTimeOffset createdAt) => new(
        new UserProfile("Local User", Currency, createdAt),
        "Local User",
        [1, 2, 3],
        [4, 5, 6],
        LocalAccountStorageMode.InMemory,
        createdAt);
}
