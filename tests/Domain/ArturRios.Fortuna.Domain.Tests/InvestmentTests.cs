using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class InvestmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 23, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidInvestment_WhenCreated_ThenValuesAreNormalizedAndOwnershipIsFixed()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);

        var investment = new Investment(
            user,
            "  Treasury Bond  ",
            "  Example Broker  ",
            InvestmentType.FixedIncome,
            currency,
            Now);

        Assert.Equal(user, investment.User);
        Assert.Equal("Treasury Bond", investment.Instrument);
        Assert.Equal("TREASURY BOND", investment.NormalizedInstrument);
        Assert.Equal("Example Broker", investment.Institution);
        Assert.Equal(InvestmentType.FixedIncome, investment.InvestmentType);
        Assert.Equal(currency, investment.Currency);
        Assert.Equal(Now, investment.CreatedAt);
        Assert.Equal(Now, investment.UpdatedAt);
        Assert.True(typeof(Investment).GetProperty(nameof(Investment.UserId))!
            .SetMethod!.IsPrivate);
        Assert.True(typeof(Investment).GetProperty(nameof(Investment.CurrencyId))!
            .SetMethod!.IsPrivate);
    }

    [UnitFact]
    public void GivenWhitespaceInstitution_WhenCreated_ThenInstitutionIsAbsent()
    {
        var (user, currency) = Owner();

        var investment = new Investment(
            user,
            "Fund",
            "   ",
            InvestmentType.Fund,
            currency,
            Now);

        Assert.Null(investment.Institution);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenMissingInstrument_WhenCreated_ThenItIsRejected(string instrument)
    {
        var (user, currency) = Owner();

        Assert.Throws<ArgumentException>(() => new Investment(
            user,
            instrument,
            null,
            InvestmentType.Other,
            currency,
            Now));
    }

    [UnitFact]
    public void GivenOversizedFields_WhenCreated_ThenTheyAreRejected()
    {
        var (user, currency) = Owner();

        Assert.Throws<ArgumentException>(() => new Investment(
            user, new string('i', 201), null, InvestmentType.Other, currency, Now));
        Assert.Throws<ArgumentException>(() => new Investment(
            user, "Instrument", new string('i', 201), InvestmentType.Other, currency, Now));
    }

    [UnitFact]
    public void GivenInvalidType_WhenCreated_ThenItIsRejected()
    {
        var (user, currency) = Owner();

        Assert.Throws<ArgumentOutOfRangeException>(() => new Investment(
            user, "Instrument", null, (InvestmentType)99, currency, Now));
    }

    [UnitFact]
    public void GivenMissingOwnerOrCurrency_WhenCreated_ThenItIsRejected()
    {
        var (user, currency) = Owner();

        Assert.Throws<ArgumentNullException>(() => new Investment(
            null!, "Instrument", null, InvestmentType.Other, currency, Now));
        Assert.Throws<ArgumentNullException>(() => new Investment(
            user, "Instrument", null, InvestmentType.Other, null!, Now));
    }

    [UnitFact]
    public void GivenValidDetails_WhenUpdated_ThenEditableFieldsAreNormalized()
    {
        var (user, currency) = Owner();
        var investment = new Investment(
            user, "Before", "Old Broker", InvestmentType.Fund, currency, Now);
        var updatedAt = Now.AddHours(1);

        investment.UpdateDetails(
            "  After  ",
            "  New Broker  ",
            InvestmentType.Equity,
            updatedAt);

        Assert.Equal("After", investment.Instrument);
        Assert.Equal("AFTER", investment.NormalizedInstrument);
        Assert.Equal("New Broker", investment.Institution);
        Assert.Equal(InvestmentType.Equity, investment.InvestmentType);
        Assert.Equal(currency, investment.Currency);
        Assert.Equal(user, investment.User);
        Assert.Equal(updatedAt, investment.UpdatedAt);
    }

    [UnitFact]
    public void GivenWhitespaceInstitution_WhenUpdated_ThenInstitutionIsCleared()
    {
        var (user, currency) = Owner();
        var investment = new Investment(
            user, "Fund", "Broker", InvestmentType.Fund, currency, Now);

        investment.UpdateDetails("Fund", "   ", InvestmentType.Fund, Now.AddHours(1));

        Assert.Null(investment.Institution);
    }

    [UnitFact]
    public void GivenInvalidDetails_WhenUpdated_ThenTheyAreRejected()
    {
        var (user, currency) = Owner();
        var investment = new Investment(
            user, "Fund", "Broker", InvestmentType.Fund, currency, Now);

        Assert.Throws<ArgumentException>(() => investment.UpdateDetails(
            " ", null, InvestmentType.Fund, Now));
        Assert.Throws<ArgumentException>(() => investment.UpdateDetails(
            new string('i', 201), null, InvestmentType.Fund, Now));
        Assert.Throws<ArgumentException>(() => investment.UpdateDetails(
            "Fund", new string('i', 201), InvestmentType.Fund, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => investment.UpdateDetails(
            "Fund", null, (InvestmentType)99, Now));
    }

    private static (UserProfile User, Currency Currency) Owner()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        return (new UserProfile(Guid.NewGuid(), "Owner", currency, Now), currency);
    }
}
