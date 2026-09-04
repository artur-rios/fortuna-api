using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class FinancialAccountTests
{
    [UnitFact]
    public void GivenValidOverdrawnAccount_WhenCreated_ThenOwnerAndCurrencyAreFixed()
    {
        var now = DateTimeOffset.UtcNow;
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Account Owner", currency, now);

        var account = new FinancialAccount(
            user,
            "  Daily Account  ",
            "  Example Bank  ",
            FinancialAccountType.Checking,
            currency,
            -125.45m,
            now);

        Assert.Equal(user, account.User);
        Assert.Equal("Daily Account", account.Name);
        Assert.Equal("DAILY ACCOUNT", account.NormalizedName);
        Assert.Equal("Example Bank", account.Institution);
        Assert.Equal(FinancialAccountType.Checking, account.AccountType);
        Assert.Equal(currency, account.Currency);
        Assert.Equal(-125.45m, account.OpeningBalance);
        Assert.Equal(now, account.CreatedAt);
        Assert.Equal(now, account.UpdatedAt);
        Assert.True(typeof(FinancialAccount).GetProperty(nameof(FinancialAccount.UserId))!
            .SetMethod!.IsPrivate);
        Assert.True(typeof(FinancialAccount).GetProperty(nameof(FinancialAccount.CurrencyId))!
            .SetMethod!.IsPrivate);
    }

    [UnitFact]
    public void GivenWhitespaceInstitution_WhenCreated_ThenInstitutionIsAbsent()
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var account = new FinancialAccount(
            new UserProfile(Guid.NewGuid(), "Owner", currency, DateTimeOffset.UtcNow),
            "Cash",
            "   ",
            FinancialAccountType.Cash,
            currency,
            0,
            DateTimeOffset.UtcNow);

        Assert.Null(account.Institution);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenMissingName_WhenCreated_ThenItIsRejected(string name)
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);

        Assert.Throws<ArgumentException>(() => new FinancialAccount(
            new UserProfile(Guid.NewGuid(), "Owner", currency, DateTimeOffset.UtcNow),
            name,
            null,
            FinancialAccountType.Cash,
            currency,
            0,
            DateTimeOffset.UtcNow));
    }

    [UnitFact]
    public void GivenInvalidAccountType_WhenCreated_ThenItIsRejected()
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialAccount(
            new UserProfile(Guid.NewGuid(), "Owner", currency, DateTimeOffset.UtcNow),
            "Cash",
            null,
            (FinancialAccountType)99,
            currency,
            0,
            DateTimeOffset.UtcNow));
    }
}
