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

    [UnitFact]
    public void GivenNewDetails_WhenUpdated_ThenOnlyEditableFieldsAndTimestampChange()
    {
        var createdAt = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddHours(2);
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, createdAt);
        var account = new FinancialAccount(
            user,
            "Before",
            "Old Bank",
            FinancialAccountType.Checking,
            currency,
            -25,
            createdAt);

        account.UpdateDetails(
            "  After  ",
            "  New Bank  ",
            FinancialAccountType.Savings,
            updatedAt);

        Assert.Equal("After", account.Name);
        Assert.Equal("AFTER", account.NormalizedName);
        Assert.Equal("New Bank", account.Institution);
        Assert.Equal(FinancialAccountType.Savings, account.AccountType);
        Assert.Equal(user, account.User);
        Assert.Equal(currency, account.Currency);
        Assert.Equal(-25, account.OpeningBalance);
        Assert.Equal(createdAt, account.CreatedAt);
        Assert.Equal(updatedAt, account.UpdatedAt);
    }

    [UnitFact]
    public void GivenWhitespaceInstitution_WhenUpdated_ThenInstitutionIsCleared()
    {
        var now = DateTimeOffset.UtcNow;
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var account = new FinancialAccount(
            new UserProfile(Guid.NewGuid(), "Owner", currency, now),
            "Cash",
            "Bank",
            FinancialAccountType.Cash,
            currency,
            0,
            now);

        account.UpdateDetails("Cash", "   ", FinancialAccountType.Cash, now.AddMinutes(1));

        Assert.Null(account.Institution);
    }

    [UnitFact]
    public void GivenInvalidDetails_WhenUpdated_ThenTheyAreRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var account = new FinancialAccount(
            new UserProfile(Guid.NewGuid(), "Owner", currency, now),
            "Cash",
            null,
            FinancialAccountType.Cash,
            currency,
            0,
            now);

        Assert.Throws<ArgumentException>(() => account.UpdateDetails(
            " ", null, FinancialAccountType.Cash, now));
        Assert.Throws<ArgumentException>(() => account.UpdateDetails(
            "Cash", new string('i', 201), FinancialAccountType.Cash, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => account.UpdateDetails(
            "Cash", null, (FinancialAccountType)99, now));
    }
}
