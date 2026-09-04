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

    [UnitFact]
    public void GivenValidDetails_WhenUpdated_ThenEditableFieldsAndTimestampChange()
    {
        var card = Create(lastFourDigits: "1234");
        var currency = card.Currency;
        var updatedAt = Now.AddHours(1);

        card.UpdateDetails("  Travel  ", "  New Bank  ", 2500m, 28, 7, updatedAt);

        Assert.Equal("Travel", card.Name);
        Assert.Equal("TRAVEL", card.NormalizedName);
        Assert.Equal("New Bank", card.Issuer);
        Assert.Equal(2500m, card.CreditLimit);
        Assert.Equal((short)28, card.ClosingDay);
        Assert.Equal((short)7, card.DueDay);
        Assert.Equal("1234", card.LastFourDigits);
        Assert.Equal(currency, card.Currency);
        Assert.Equal(updatedAt, card.UpdatedAt);
    }

    [UnitTheory]
    [InlineData("", "Bank", 1000, (short)20, (short)5, "name")]
    [InlineData("Card", "", 1000, (short)20, (short)5, "issuer")]
    [InlineData("Card", "Bank", 0, (short)20, (short)5, "creditLimit")]
    [InlineData("Card", "Bank", 1000, (short)0, (short)5, "closingDay")]
    [InlineData("Card", "Bank", 1000, (short)20, (short)32, "dueDay")]
    public void GivenInvalidDetails_WhenUpdated_ThenNamedFieldIsRejected(
        string name,
        string issuer,
        decimal limit,
        short closingDay,
        short dueDay,
        string field)
    {
        var card = Create();

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            card.UpdateDetails(name, issuer, limit, closingDay, dueDay, Now.AddHours(1)));

        Assert.Equal(field, exception.ParamName);
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
