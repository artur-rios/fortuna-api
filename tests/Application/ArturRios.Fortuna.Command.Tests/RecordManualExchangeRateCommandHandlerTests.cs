using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordManualExchangeRateCommandHandlerTests
{
    [UnitFact]
    public async Task GivenSupportedPair_WhenRecorded_ThenNormalizedManualRateTakesPrecedence()
    {
        var store = new StubRateStore(new ManualRateUpsertResult(5.25m, false));
        var handler = Handler(store, ["USD", "BRL"]);

        var result = await handler.HandleAsync(ValidCommand("usd", "brl"));

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("USD", result.Data.BaseCurrencyCode);
        Assert.Equal("BRL", result.Data.QuoteCurrencyCode);
        Assert.Equal(5.25m, result.Data.Rate);
        Assert.True(result.Data.TakesPrecedence);
        Assert.False(result.Data.ReplacedExisting);
        Assert.Equal("USD", store.Candidate!.BaseCurrencyCode);
        Assert.Contains(ManualExchangeRateMessages.RecordedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenExistingManualRate_WhenRecorded_ThenReplacementIsReported()
    {
        var store = new StubRateStore(new ManualRateUpsertResult(5.4m, true));
        var command = ValidCommand();
        command.Rate = 5.4m;

        var result = await Handler(store, ["USD", "BRL"]).HandleAsync(command);

        Assert.True(result.Success);
        Assert.True(result.Data!.ReplacedExisting);
        Assert.Equal(5.4m, result.Data.Rate);
        Assert.Contains(ManualExchangeRateMessages.ReplacedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData("ZZZ", "BRL", "ZZZ")]
    [InlineData("USD", "ZZZ", "ZZZ")]
    public async Task GivenUnknownCurrency_WhenRecorded_ThenUnknownCodeIsNamed(
        string baseCode,
        string quoteCode,
        string unknownCode)
    {
        var store = new StubRateStore(new ManualRateUpsertResult(5.25m, false));

        var result = await Handler(store, ["USD", "BRL"])
            .HandleAsync(ValidCommand(baseCode, quoteCode));

        Assert.False(result.Success);
        Assert.Contains(ManualExchangeRateMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(ManualExchangeRateMessages.UnknownCurrency(unknownCode), result.Messages);
        Assert.Null(store.Candidate);
    }

    [UnitFact]
    public async Task GivenInvalidInput_WhenRecorded_ThenNothingIsStored()
    {
        var store = new StubRateStore(new ManualRateUpsertResult(0m, false));
        var command = ValidCommand();
        command.Rate = 0;

        var result = await Handler(store, ["USD", "BRL"]).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(ManualExchangeRateMessages.RateMustBePositive, result.Errors);
        Assert.Null(store.Candidate);
    }

    private static RecordManualExchangeRateCommandHandler Handler(
        StubRateStore store,
        IReadOnlyCollection<string> supported) => new(
            new RecordManualExchangeRateCommandValidator(),
            new StubCurrencyReader(supported),
            store);

    private static RecordManualExchangeRateCommand ValidCommand(
        string baseCode = "USD",
        string quoteCode = "BRL") => new()
        {
            BaseCurrencyCode = baseCode,
            QuoteCurrencyCode = quoteCode,
            Rate = 5.25m,
            RateDate = new DateOnly(2026, 9, 4)
        };

    private sealed class StubCurrencyReader(IReadOnlyCollection<string> supported) : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) => Task.FromResult(
                supported.Contains(code, StringComparer.Ordinal)
                    ? new CurrencySnapshot(code, code, 2)
                    : null);
    }

    private sealed class StubRateStore(ManualRateUpsertResult result) : IExchangeRateStore
    {
        public ManualRateCandidate? Candidate { get; private set; }

        public Task<PublishedRateUpsertResult> UpsertPublishedAsync(
            IReadOnlyCollection<PublishedRateCandidate> rates,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualRateUpsertResult> UpsertManualAsync(
            ManualRateCandidate rate,
            CancellationToken cancellationToken)
        {
            Candidate = rate;
            return Task.FromResult(result);
        }
    }
}
