using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class NetPositionQueryHandlerTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        "Owner",
        "BRL",
        false,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);

    [UnitFact]
    public async Task GivenMixedHoldings_WhenGettingNetPosition_ThenGroupsConvertOnceAndTotalOnce()
    {
        // Given
        var positionReader = new StubPositionReader([
            new("BRL", 100.125m, 50m, 25m),
            new("USD", 10m, 2m, 20m)
        ]);
        var rateReader = new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 5m, Today.AddDays(-1), ExchangeRateSource.Manual));
        var handler = Handler(positionReader, rateReader: rateReader);

        // When
        var result = await handler.HandleAsync(new GetNetPositionQuery());

        // Then
        Assert.True(result.Success);
        Assert.Equal(Today, result.Data!.AsOf);
        Assert.Equal("BRL", result.Data.DisplayCurrencyCode);
        Assert.True(result.Data.IsFullyConverted);
        Assert.Equal(85.13m, result.Data.Total);
        Assert.Equal(Profile.Id, positionReader.UserId);
        Assert.Equal(Today, positionReader.AsOf);
        var groups = result.Data.CurrencyGroups.ToArray();
        Assert.Equal(125.125m, groups[0].SourceNet);
        Assert.Equal(125.13m, groups[0].DisplayNet);
        Assert.Equal(-8m, groups[1].SourceNet);
        Assert.Equal(-40m, groups[1].DisplayNet);
        Assert.Equal(5m, groups[1].AppliedRate);
        Assert.Equal(Today.AddDays(-1), groups[1].RateDate);
        Assert.Equal(1, rateReader.CallCount);
        Assert.Contains(NetPositionMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingRate_WhenGettingNetPosition_ThenSourceGroupsReturnWithoutFalseTotal()
    {
        // Given
        var handler = Handler(new StubPositionReader([
            new("USD", 10m, 0m, 0m)
        ]));

        // When
        var result = await handler.HandleAsync(new GetNetPositionQuery
        {
            DisplayCurrencyCode = "BRL",
            AsOf = Today.AddDays(-2)
        });

        // Then
        Assert.True(result.Success);
        Assert.False(result.Data!.IsFullyConverted);
        Assert.Null(result.Data.Total);
        var group = Assert.Single(result.Data.CurrencyGroups);
        Assert.Equal(10m, group.SourceNet);
        Assert.Null(group.DisplayNet);
        Assert.Equal(FigureConversionMessages.RateUnavailable, group.UnconvertedReason);
        Assert.Contains(NetPositionMessages.PartiallyConverted, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoHoldings_WhenGettingNetPosition_ThenZeroInDisplayCurrencyReturns()
    {
        // Given
        var handler = Handler(new StubPositionReader([]));

        // When
        var result = await handler.HandleAsync(new GetNetPositionQuery());

        // Then
        Assert.True(result.Success);
        Assert.True(result.Data!.IsFullyConverted);
        Assert.Equal(0m, result.Data.Total);
        Assert.Empty(result.Data.CurrencyGroups);
    }

    [UnitFact]
    public async Task GivenInvalidCurrencyMissingProfileOrUnsupportedCurrency_WhenHandled_ThenReadDoesNotRun()
    {
        // Given
        var invalidReader = new StubPositionReader([]);
        var missingReader = new StubPositionReader([]);
        var unsupportedReader = new StubPositionReader([]);

        // When
        var invalid = await Handler(invalidReader).HandleAsync(new GetNetPositionQuery
        {
            DisplayCurrencyCode = "US"
        });
        var missing = await Handler(missingReader, missingProfile: true)
            .HandleAsync(new GetNetPositionQuery());
        var unsupported = await Handler(unsupportedReader,
                currencyReader: new StubCurrencyReader(false))
            .HandleAsync(new GetNetPositionQuery { DisplayCurrencyCode = "ZZZ" });

        // Then
        Assert.Contains(NetPositionMessages.DisplayCurrencyInvalid, invalid.Errors);
        Assert.Contains(NetPositionMessages.ProfileNotFound, missing.Errors);
        Assert.Contains(NetPositionMessages.DisplayCurrencyUnsupported, unsupported.Errors);
        Assert.All([invalidReader, missingReader, unsupportedReader], reader =>
            Assert.Null(reader.UserId));
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenGettingNetPosition_ThenProfileUsesPublicIdentifier()
    {
        // Given
        var profiles = new StubProfileReader(Profile);
        var actor = new RequestActor(Profile.Id, 3, null, []) { IsLocal = true };

        // When
        var result = await Handler(new StubPositionReader([]), profiles: profiles, actor: actor)
            .HandleAsync(new GetNetPositionQuery());

        // Then
        Assert.True(result.Success);
        Assert.True(profiles.PublicLookupUsed);
    }

    private static GetNetPositionQueryHandler Handler(
        StubPositionReader positionReader,
        StubRateReader? rateReader = null,
        StubCurrencyReader? currencyReader = null,
        bool missingProfile = false,
        StubProfileReader? profiles = null,
        RequestActor? actor = null)
    {
        var profile = missingProfile ? null : Profile;
        return new GetNetPositionQueryHandler(
            new GetNetPositionQueryValidator(),
            profiles ?? new StubProfileReader(profile),
            positionReader,
            currencyReader ?? new StubCurrencyReader(true),
            rateReader ?? new StubRateReader(null),
            new StubActor(actor ?? new RequestActor(
                profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
            new FixedTimeProvider(new DateTimeOffset(
                Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc))));
    }

    private sealed class StubPositionReader(
        IReadOnlyCollection<NetPositionCurrencySnapshot> positions) : INetPositionReader
    {
        public Guid? UserId { get; private set; }
        public DateOnly? AsOf { get; private set; }

        public Task<IReadOnlyCollection<NetPositionCurrencySnapshot>> ReadAsync(
            Guid userId,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            AsOf = asOf;
            return Task.FromResult(positions);
        }
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public int CallCount { get; private set; }

        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(rate);
        }
    }

    private sealed class StubCurrencyReader(bool supportsCurrency) : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) => Task.FromResult<
                IReadOnlyCollection<CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) => Task.FromResult(
                supportsCurrency ? new CurrencySnapshot(code, code, 2) : null);
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicLookupUsed = true;
            return Task.FromResult(profile);
        }
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
