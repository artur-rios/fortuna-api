using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CommittedObligationsQueryHandlerTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        "Owner", "BRL", false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    [UnitFact]
    public async Task GivenMixedCommitments_WhenListed_ThenOverdueConversionOrderAndTotalsAreReported()
    {
        // Given
        var overdueId = Guid.NewGuid();
        var reader = new StubReader([
            new(Guid.NewGuid(), CommittedObligationKind.Installment, Today.AddDays(30),
                Today.AddDays(20), Today.AddDays(28), "BRL", 10m),
            new(overdueId, CommittedObligationKind.Statement, Today.AddDays(-2),
                Today.AddDays(-30), Today.AddDays(-5), "USD", 20m)
        ]);
        var handler = Handler(reader, new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 5m, Today.AddDays(-3), ExchangeRateSource.Manual)));

        // When
        var result = await handler.HandleAsync(new ListCommittedObligationsQuery
        {
            HorizonDays = 40
        });

        // Then
        Assert.True(result.Success);
        Assert.True(result.Data!.IsFullyConverted);
        Assert.Equal(110m, result.Data.Total);
        var items = result.Data.Items.ToArray();
        Assert.Equal(overdueId, items[0].Id);
        Assert.True(items[0].IsOverdue);
        Assert.Equal(2, items[0].DaysOverdue);
        Assert.Equal(100m, items[0].DisplayAmount);
        Assert.Equal(2, result.Data.Periods.Count);
        Assert.Single(result.Data.Rates);
        Assert.Equal(Profile.Id, reader.UserId);
    }

    [UnitFact]
    public async Task GivenNoCommitments_WhenListed_ThenEmptyResultHasZeroTotal()
    {
        // Given
        var handler = Handler(new StubReader([]));

        // When
        var result = await handler.HandleAsync(new ListCommittedObligationsQuery
        {
            HorizonDays = 30
        });

        // Then
        Assert.True(result.Success);
        Assert.Equal(0m, result.Data!.Total);
        Assert.Empty(result.Data.Items);
        Assert.Empty(result.Data.Periods);
    }

    [UnitFact]
    public async Task GivenMissingRate_WhenListed_ThenSourceAmountReturnsWithoutFalseTotals()
    {
        // Given
        var handler = Handler(new StubReader([
            new(Guid.NewGuid(), CommittedObligationKind.Statement, Today.AddDays(1),
                Today.AddDays(-20), Today, "USD", 20m)
        ]));

        // When
        var result = await handler.HandleAsync(new ListCommittedObligationsQuery
        {
            HorizonDays = 30
        });

        // Then
        Assert.True(result.Success);
        Assert.False(result.Data!.IsFullyConverted);
        Assert.Null(result.Data.Total);
        Assert.Null(Assert.Single(result.Data.Items).DisplayAmount);
        Assert.Null(Assert.Single(result.Data.Periods).Total);
        Assert.Contains(CommittedObligationMessages.PartiallyConverted, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidHorizonProfileOrCurrency_WhenListed_ThenReadIsRejected()
    {
        // Given
        var reader = new StubReader([]);

        // When
        var invalid = await Handler(reader).HandleAsync(new ListCommittedObligationsQuery
        {
            HorizonDays = 367
        });
        var missing = await Handler(reader, missingProfile: true)
            .HandleAsync(new ListCommittedObligationsQuery { HorizonDays = 30 });
        var unsupported = await Handler(reader, supportsCurrency: false)
            .HandleAsync(new ListCommittedObligationsQuery { HorizonDays = 30 });

        // Then
        Assert.Contains(CommittedObligationMessages.HorizonMaximum(366), invalid.Errors);
        Assert.Contains(CommittedObligationMessages.ProfileNotFound, missing.Errors);
        Assert.Contains(CommittedObligationMessages.DisplayCurrencyUnsupported,
            unsupported.Errors);
    }

    private static ListCommittedObligationsQueryHandler Handler(
        StubReader reader,
        StubRateReader? rates = null,
        bool missingProfile = false,
        bool supportsCurrency = true)
    {
        var options = new CashFlowProjectionOptions(366, 90, 30);
        return new ListCommittedObligationsQueryHandler(
            new ListCommittedObligationsQueryValidator(options),
            new StubProfileReader(missingProfile ? null : Profile),
            reader,
            new StubCurrencyReader(supportsCurrency),
            rates ?? new StubRateReader(null),
            new StubActor(new RequestActor(Profile.ExternalSubject!.Value, 3, null, [])),
            new FixedTimeProvider(new DateTimeOffset(
                Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc))));
    }

    private sealed class StubReader(IReadOnlyCollection<CommittedObligationSnapshot> items)
        : ICommittedObligationReader
    {
        public Guid? UserId { get; private set; }

        public Task<IReadOnlyCollection<CommittedObligationSnapshot>> ReadAsync(
            Guid userId, DateOnly asOf, DateOnly through, CancellationToken cancellationToken)
        {
            UserId = userId;
            return Task.FromResult(items);
        }
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(string baseCurrencyCode,
            string quoteCurrencyCode, DateOnly figureDate, CancellationToken cancellationToken) =>
            Task.FromResult(rate);
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
