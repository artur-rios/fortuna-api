using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateCreditCardCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 17, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidFollowingMonthDueDay_WhenCreated_ThenNormalizedCardIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var store = new StubCreditCardStore(new CreditCardCreationResult(
            new CreditCardSnapshot(
                cardId,
                userId,
                "Rewards",
                "Example Bank",
                "BRL",
                5000.1234m,
                28,
                5,
                "1234",
                false,
                Now,
                Now),
            false));
        var handler = Handler(subject, Profile(userId, subject), store, ["BRL"]);

        var result = await handler.HandleAsync(new CreateCreditCardCommand
        {
            Name = "  Rewards  ",
            Issuer = "  Example Bank  ",
            CurrencyCode = "brl",
            CreditLimit = 5000.1234m,
            ClosingDay = 28,
            DueDay = 5,
            LastFourDigits = "1234"
        });

        Assert.True(result.Success);
        Assert.Equal(cardId, result.Data!.Id);
        Assert.Equal("Rewards", result.Data.Name);
        Assert.Equal("Example Bank", result.Data.Issuer);
        Assert.Equal("BRL", result.Data.CurrencyCode);
        Assert.Equal(5000.1234m, result.Data.CreditLimit);
        Assert.Equal((short)28, result.Data.ClosingDay);
        Assert.Equal((short)5, result.Data.DueDay);
        Assert.Equal("1234", result.Data.LastFourDigits);
        Assert.Equal(Now, result.Data.CreatedAt);
        Assert.Equal(userId, store.Creation!.UserId);
        Assert.Equal("Rewards", store.Creation.Name);
        Assert.Equal("Example Bank", store.Creation.Issuer);
        Assert.Equal("BRL", store.Creation.CurrencyCode);
        Assert.Equal(Now, store.Creation.CreatedAt);
        Assert.Contains(CreditCardMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateLiveName_WhenCreated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubCreditCardStore(new CreditCardCreationResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.DuplicateName, result.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownCurrency_WhenCreated_ThenUnknownCodeIsNamed()
    {
        var subject = Guid.NewGuid();
        var store = new StubCreditCardStore(new CreditCardCreationResult(null, false));
        var command = ValidCommand();
        command.CurrencyCode = "zzz";

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(CreditCardMessages.UnknownCurrency("ZZZ"), result.Messages);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenNotFoundIsReturned()
    {
        var store = new StubCreditCardStore(new CreditCardCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenCreated_ThenNothingIsStored()
    {
        var store = new StubCreditCardStore(new CreditCardCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, []).HandleAsync(
            new CreateCreditCardCommand
            {
                Name = string.Empty,
                Issuer = new string('i', 201),
                CurrencyCode = string.Empty,
                CreditLimit = 0,
                ClosingDay = 0,
                DueDay = 32,
                LastFourDigits = "12a4"
            });

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.NameRequired, result.Errors);
        Assert.Contains(CreditCardMessages.IssuerTooLong, result.Errors);
        Assert.Contains(CreditCardMessages.CurrencyRequired, result.Errors);
        Assert.Contains(CreditCardMessages.CreditLimitPositive, result.Errors);
        Assert.Contains(CreditCardMessages.ClosingDayInvalid, result.Errors);
        Assert.Contains(CreditCardMessages.DueDayInvalid, result.Errors);
        Assert.Contains(CreditCardMessages.LastFourDigitsInvalid, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenCreated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubCreditCardStore(new CreditCardCreationResult(
            new CreditCardSnapshot(
                Guid.NewGuid(), userId, "Rewards", "Example Bank", "BRL",
                1000, 20, 5, null, false, Now, Now),
            false));
        var handler = new CreateCreditCardCommandHandler(
            new CreateCreditCardCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            new StubCurrencyReader(["BRL"]),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static CreateCreditCardCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        ICreditCardStore store,
        IReadOnlyCollection<string> currencies) => new(
            new CreateCreditCardCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            new StubCurrencyReader(currencies),
            store,
            new FixedTimeProvider(Now));

    private static CreateCreditCardCommand ValidCommand() => new()
    {
        Name = "Rewards",
        Issuer = "Example Bank",
        CurrencyCode = "BRL",
        CreditLimit = 1000,
        ClosingDay = 20,
        DueDay = 5
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubCreditCardStore(CreditCardCreationResult result) : ICreditCardStore
    {
        public CreditCardCreation? Creation { get; private set; }

        public Task<CreditCardCreationResult> CreateAsync(
            CreditCardCreation creation,
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
