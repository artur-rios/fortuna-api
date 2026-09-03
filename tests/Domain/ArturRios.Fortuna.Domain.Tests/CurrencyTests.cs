using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class CurrencyTests
{
    [UnitFact]
    public void GivenValidCurrency_WhenCreated_ThenCodeIsNormalizedAndMetadataIsPreserved()
    {
        var currency = new Currency("brl", "Brazilian real", 2);

        Assert.Equal("BRL", currency.Code);
        Assert.Equal("Brazilian real", currency.Name);
        Assert.Equal(2, currency.MinorUnitDigits);
    }

    [UnitFact]
    public void GivenCodeWithWrongLength_WhenCreated_ThenArgumentExceptionIsThrown()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Currency("BR", "Real", 2));

        Assert.Equal("code", exception.ParamName);
    }

    [UnitTheory]
    [InlineData(-1)]
    [InlineData(5)]
    public void GivenUnsupportedMinorUnits_WhenCreated_ThenArgumentOutOfRangeExceptionIsThrown(short digits)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Currency("BRL", "Real", digits));

        Assert.Equal("minorUnitDigits", exception.ParamName);
    }

    [UnitFact]
    public void GivenBlankName_WhenCreated_ThenArgumentExceptionIsThrown()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Currency("BRL", " ", 2));

        Assert.Equal("name", exception.ParamName);
    }
}
