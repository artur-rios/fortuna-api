using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class CreditCardTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 17, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenDueDayBeforeClosingDay_WhenCreated_ThenCardIsAcceptedForFollowingMonth()
    {
        var user = User();
        var currency = Currency();

        var card = new CreditCard(
            user,
            "  Rewards  ",
            "  Example Bank  ",
            currency,
            5000.1234m,
            28,
            5,
            " 1234 ",
            Now);

        Assert.Equal(user, card.User);
        Assert.Equal("Rewards", card.Name);
        Assert.Equal("REWARDS", card.NormalizedName);
        Assert.Equal("Example Bank", card.Issuer);
        Assert.Equal(currency, card.Currency);
        Assert.Equal(5000.1234m, card.CreditLimit);
        Assert.Equal(28, card.ClosingDay);
        Assert.Equal(5, card.DueDay);
        Assert.Equal("1234", card.LastFourDigits);
        Assert.False(card.IsDeleted);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenMissingName_WhenCreated_ThenCardIsRejected(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(name: name));

        Assert.Equal("name", exception.ParamName);
    }

    [UnitFact]
    public void GivenMissingIssuer_WhenCreated_ThenCardIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(issuer: " "));

        Assert.Equal("issuer", exception.ParamName);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveLimit_WhenCreated_ThenCardIsRejected(decimal limit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Create(creditLimit: limit));

        Assert.Equal("creditLimit", exception.ParamName);
    }

    [UnitTheory]
    [InlineData((short)0, (short)10, "closingDay")]
    [InlineData((short)32, (short)10, "closingDay")]
    [InlineData((short)10, (short)0, "dueDay")]
    [InlineData((short)10, (short)32, "dueDay")]
    public void GivenInvalidBillingDay_WhenCreated_ThenFieldIsRejected(
        short closingDay,
        short dueDay,
        string field)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(closingDay: closingDay, dueDay: dueDay));

        Assert.Equal(field, exception.ParamName);
    }

    [UnitTheory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    public void GivenInvalidLastFourDigits_WhenCreated_ThenCardIsRejected(string digits)
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(lastFourDigits: digits));

        Assert.Equal("lastFourDigits", exception.ParamName);
    }

    private static CreditCard Create(
        string name = "Rewards",
        string issuer = "Example Bank",
        decimal creditLimit = 1000m,
        short closingDay = 20,
        short dueDay = 5,
        string? lastFourDigits = null) => new(
        User(),
        name,
        issuer,
        Currency(),
        creditLimit,
        closingDay,
        dueDay,
        lastFourDigits,
        Now);

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Account Owner",
        Currency(),
        Now);

    private static Currency Currency() => new("BRL", "Brazilian real", 2);
}
