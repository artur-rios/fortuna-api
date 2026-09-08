using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CashFlowProjectionQueryHandlerTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        "Owner", "BRL", false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    [UnitFact]
    public async Task GivenMixedProjectionInputs_WhenProjectingCashFlow_ThenPeriodsAndKindsRemainDistinct()
    {
        // Given
        var reader = new StubProjectionReader(new CashFlowProjectionSnapshot(
            [new("BRL", 100m), new("USD", 10m)],
            [
                new(new DateOnly(2026, 9, 15), "BRL", -10m, CashFlowSourceKind.Recurring),
                new(new DateOnly(2026, 9, 20), "BRL", -5m, CashFlowSourceKind.Installment),
                new(new DateOnly(2026, 10, 5), "BRL", -20m, CashFlowSourceKind.Statement)
            ],
            [new(Today, "BRL", -90m, CashFlowSourceKind.Historical)],
            Today.AddDays(-89)));
        var rates = new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 5m, Today, ExchangeRateSource.Manual));
        var handler = Handler(reader, rates: rates);

        // When
        var result = await handler.HandleAsync(new ProjectCashFlowQuery
        {
            HorizonDays = 40,
            IncludeEstimate = true
        });

        // Then
        Assert.True(result.Success);
        Assert.Equal(150m, result.Data!.StartingBalance);
        var periods = result.Data.Periods.ToArray();
        Assert.Equal(2, periods.Length);
        Assert.Equal(113m, periods[0].ClosingBalance);
        Assert.Equal(75m, periods[1].ClosingBalance);
        Assert.Contains(periods[0].Figures, item =>
            item.Kind == CashFlowFigureKind.Recorded && item.Amount == 150m);
        Assert.Contains(periods[0].Figures, item =>
            item.Kind == CashFlowFigureKind.Projected && item.Amount == -10m);
        Assert.Contains(periods[0].Figures, item =>
            item.Kind == CashFlowFigureKind.Committed && item.Amount == -5m);
        Assert.Contains(periods[0].Figures, item =>
            item.Kind == CashFlowFigureKind.Estimated && item.Amount == -22m);
        Assert.Single(result.Data.Rates);
        Assert.Equal(Profile.Id, reader.UserId);
        Assert.Equal(Today.AddDays(40), reader.Through);
    }

    [UnitFact]
    public async Task GivenNoFutureInputs_WhenProjectingCashFlow_ThenAFlatLineExplainsWhy()
    {
        // Given
        var handler = Handler(new StubProjectionReader(new CashFlowProjectionSnapshot(
            [new("BRL", 25m)], [], [], null)));

        // When
        var result = await handler.HandleAsync(new ProjectCashFlowQuery { HorizonDays = 30 });

        // Then
        Assert.True(result.Success);
        Assert.All(result.Data!.Periods, period => Assert.Equal(25m, period.ClosingBalance));
        Assert.Equal(CashFlowProjectionMessages.NoProjectionInputs, result.Data.FlatReason);
    }

    [UnitFact]
    public async Task GivenTooLittleHistory_WhenEstimateRequested_ThenEstimateIsOmittedWithReason()
    {
        // Given
        var handler = Handler(new StubProjectionReader(new CashFlowProjectionSnapshot(
            [new("BRL", 25m)], [],
            [new(Today, "BRL", -10m, CashFlowSourceKind.Historical)],
            Today.AddDays(-28))));

        // When
        var result = await handler.HandleAsync(new ProjectCashFlowQuery
        {
            HorizonDays = 30,
            IncludeEstimate = true
        });

        // Then
        Assert.True(result.Success);
        Assert.Equal(CashFlowProjectionMessages.InsufficientHistory,
            result.Data!.EstimateOmittedReason);
        Assert.DoesNotContain(result.Data.Periods.SelectMany(item => item.Figures),
            item => item.Kind == CashFlowFigureKind.Estimated);
    }

    [UnitFact]
    public async Task GivenInvalidHorizonOrCurrency_WhenProjectingCashFlow_ThenValidationStopsTheRead()
    {
        // Given
        var reader = new StubProjectionReader(new CashFlowProjectionSnapshot([], [], [], null));
        var handler = Handler(reader);

        // When
        var zero = await handler.HandleAsync(new ProjectCashFlowQuery { HorizonDays = 0 });
        var excessive = await handler.HandleAsync(new ProjectCashFlowQuery { HorizonDays = 367 });
        var currency = await handler.HandleAsync(new ProjectCashFlowQuery
        {
            HorizonDays = 30,
            DisplayCurrencyCode = "US"
        });

        // Then
        Assert.Contains(CashFlowProjectionMessages.HorizonRequired, zero.Errors);
        Assert.Contains(CashFlowProjectionMessages.HorizonMaximum(366), excessive.Errors);
        Assert.Contains(CashFlowProjectionMessages.DisplayCurrencyInvalid, currency.Errors);
        Assert.Null(reader.UserId);
    }

    [UnitFact]
    public async Task GivenMissingProfileUnsupportedCurrencyOrRate_WhenProjecting_ThenNoFalseProjectionReturns()
    {
        // Given
        var snapshot = new CashFlowProjectionSnapshot([new("USD", 10m)], [], [], null);

        // When
        var missingProfile = await Handler(new StubProjectionReader(snapshot), missingProfile: true)
            .HandleAsync(new ProjectCashFlowQuery { HorizonDays = 30 });
        var unsupported = await Handler(new StubProjectionReader(snapshot), supportsCurrency: false)
            .HandleAsync(new ProjectCashFlowQuery { HorizonDays = 30 });
        var missingRate = await Handler(new StubProjectionReader(snapshot))
            .HandleAsync(new ProjectCashFlowQuery { HorizonDays = 30 });

        // Then
        Assert.Contains(CashFlowProjectionMessages.ProfileNotFound, missingProfile.Errors);
        Assert.Contains(CashFlowProjectionMessages.DisplayCurrencyUnsupported, unsupported.Errors);
        Assert.Contains(CashFlowProjectionMessages.ExchangeRateUnavailable, missingRate.Errors);
    }

    private static ProjectCashFlowQueryHandler Handler(
        StubProjectionReader reader,
        StubRateReader? rates = null,
        bool missingProfile = false,
        bool supportsCurrency = true)
    {
        var options = new CashFlowProjectionOptions(366, 90, 30);
        return new ProjectCashFlowQueryHandler(
            new ProjectCashFlowQueryValidator(options),
            new StubProfileReader(missingProfile ? null : Profile),
            reader,
            new StubCurrencyReader(supportsCurrency),
            rates ?? new StubRateReader(null),
            new StubActor(new RequestActor(Profile.ExternalSubject!.Value, 3, null, [])),
            new FixedTimeProvider(new DateTimeOffset(
                Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc))),
            options);
    }

    private sealed class StubProjectionReader(CashFlowProjectionSnapshot snapshot)
        : ICashFlowProjectionReader
    {
        public Guid? UserId { get; private set; }
        public DateOnly? Through { get; private set; }

        public Task<CashFlowProjectionSnapshot> ReadAsync(
            Guid userId, DateOnly asOf, DateOnly through, DateOnly historyFrom,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            Through = through;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode, string quoteCurrencyCode, DateOnly figureDate,
            CancellationToken cancellationToken) => Task.FromResult(rate);
    }

    private sealed class StubCurrencyReader(bool supports) : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(string code,
            CancellationToken cancellationToken) => Task.FromResult(
            supports ? new CurrencySnapshot(code, code, 2) : null);
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
