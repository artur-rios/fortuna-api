using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateInvestmentCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 23, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidInvestment_WhenCreated_ThenNormalizedInvestmentIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investmentId = Guid.NewGuid();
        var store = new StubInvestmentStore(new InvestmentCreationResult(
            new InvestmentSnapshot(
                investmentId,
                userId,
                "Treasury Bond",
                "Example Broker",
                InvestmentType.FixedIncome,
                "BRL",
                false,
                Now,
                Now),
            false));

        var result = await Handler(subject, Profile(userId, subject), store, ["BRL"])
            .HandleAsync(new CreateInvestmentCommand
            {
                Instrument = "  Treasury Bond  ",
                Institution = "Example Broker",
                InvestmentType = InvestmentType.FixedIncome,
                CurrencyCode = "brl"
            });

        Assert.True(result.Success);
        Assert.Equal(investmentId, result.Data!.Id);
        Assert.Equal("Treasury Bond", result.Data.Instrument);
        Assert.Equal("Example Broker", result.Data.Institution);
        Assert.Equal(InvestmentType.FixedIncome, result.Data.InvestmentType);
        Assert.Equal("BRL", result.Data.CurrencyCode);
        Assert.Equal(Now, result.Data.CreatedAt);
        Assert.Equal(Now, result.Data.UpdatedAt);
        Assert.Equal(userId, store.Creation!.UserId);
        Assert.Equal("Treasury Bond", store.Creation.Instrument);
        Assert.Equal("BRL", store.Creation.CurrencyCode);
        Assert.Equal(Now, store.Creation.CreatedAt);
        Assert.Contains(InvestmentMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateLiveInstrument_WhenCreated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubInvestmentStore(new InvestmentCreationResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.DuplicateInstrument, result.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownCurrency_WhenCreated_ThenUnknownCodeIsNamed()
    {
        var subject = Guid.NewGuid();
        var store = new StubInvestmentStore(new InvestmentCreationResult(null, false));
        var command = ValidCommand();
        command.CurrencyCode = "zzz";

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(InvestmentMessages.UnknownCurrency("ZZZ"), result.Messages);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenNotFoundIsReturned()
    {
        var store = new StubInvestmentStore(new InvestmentCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenCreated_ThenNothingIsStored()
    {
        var store = new StubInvestmentStore(new InvestmentCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, []).HandleAsync(
            new CreateInvestmentCommand
            {
                Instrument = string.Empty,
                Institution = new string('i', 201),
                InvestmentType = (InvestmentType)99,
                CurrencyCode = string.Empty
            });

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.InstrumentRequired, result.Errors);
        Assert.Contains(InvestmentMessages.InstitutionTooLong, result.Errors);
        Assert.Contains(InvestmentMessages.InvestmentTypeInvalid, result.Errors);
        Assert.Contains(InvestmentMessages.CurrencyRequired, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenCreated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubInvestmentStore(new InvestmentCreationResult(
            new InvestmentSnapshot(
                Guid.NewGuid(), userId, "Fund", null, InvestmentType.Fund,
                "BRL", false, Now, Now),
            false));
        var handler = new CreateInvestmentCommandHandler(
            new CreateInvestmentCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            new StubCurrencyReader(["BRL"]),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static CreateInvestmentCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IInvestmentStore store,
        IReadOnlyCollection<string> currencies) => new(
            new CreateInvestmentCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            new StubCurrencyReader(currencies),
            store,
            new FixedTimeProvider(Now));

    private static CreateInvestmentCommand ValidCommand() => new()
    {
        Instrument = "Fund",
        InvestmentType = InvestmentType.Fund,
        CurrencyCode = "BRL"
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubInvestmentStore(InvestmentCreationResult result) : IInvestmentStore
    {
        public InvestmentCreation? Creation { get; private set; }

        public Task<InvestmentCreationResult> CreateAsync(
            InvestmentCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(result);
        }
    }

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

    private sealed class StubUserProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
