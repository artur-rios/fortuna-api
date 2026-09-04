using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CurrencyQueryHandlerTests
{
    private static readonly CurrencySnapshot Brl = new("BRL", "Brazilian Real", 2);
    private static readonly CurrencySnapshot Jpy = new("JPY", "Yen", 0);

    [UnitFact]
    public async Task GivenSupportedCurrencies_WhenListed_ThenEveryReferenceFieldIsReturned()
    {
        var reader = new StubCurrencyReader([Brl, Jpy], Brl);

        var result = await new ListSupportedCurrenciesQueryHandler(reader)
            .HandleAsync(new ListSupportedCurrenciesQuery());

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Currencies.Count);
        Assert.Collection(result.Data.Currencies,
            currency =>
            {
                Assert.Equal("BRL", currency.Code);
                Assert.Equal("Brazilian Real", currency.Name);
                Assert.Equal(2, currency.MinorUnitDigits);
            },
            currency =>
            {
                Assert.Equal("JPY", currency.Code);
                Assert.Equal("Yen", currency.Name);
                Assert.Equal(0, currency.MinorUnitDigits);
            });
        Assert.Contains(CurrencyMessages.CurrenciesRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenSupportedCode_WhenRead_ThenCurrencyIsReturned()
    {
        var reader = new StubCurrencyReader([], Brl);

        var result = await new GetCurrencyByCodeQueryHandler(reader)
            .HandleAsync(new GetCurrencyByCodeQuery { Code = "brl" });

        Assert.True(result.Success);
        Assert.Equal("BRL", result.Data!.Code);
        Assert.Equal("Brazilian Real", result.Data.Name);
        Assert.Equal(2, result.Data.MinorUnitDigits);
        Assert.Equal("BRL", reader.RequestedCode);
        Assert.Contains(CurrencyMessages.CurrencyRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenUnknownCode_WhenRead_ThenNotFoundIsReturned()
    {
        var reader = new StubCurrencyReader([], null);

        var result = await new GetCurrencyByCodeQueryHandler(reader)
            .HandleAsync(new GetCurrencyByCodeQuery { Code = "ZZZ" });

        Assert.False(result.Success);
        Assert.Contains(CurrencyMessages.CurrencyNotFound, result.Errors);
    }

    private sealed class StubCurrencyReader(
        IReadOnlyCollection<CurrencySnapshot> currencies,
        CurrencySnapshot? currency) : ICurrencyReader
    {
        public string? RequestedCode { get; private set; }

        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) => Task.FromResult(currencies);

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken)
        {
            RequestedCode = code;
            return Task.FromResult(currency);
        }
    }
}
