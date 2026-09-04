using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateFinancialAccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidOverdrawnAccount_WhenCreated_ThenNormalizedAccountIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var store = new StubAccountStore(new FinancialAccountCreationResult(
            new FinancialAccountSnapshot(
                accountId,
                userId,
                "Daily Account",
                "Example Bank",
                FinancialAccountType.Checking,
                "BRL",
                -125.45m,
                false,
                Now,
                Now),
            false));
        var handler = Handler(subject, Profile(userId, subject), store, ["BRL"]);

        var result = await handler.HandleAsync(new CreateFinancialAccountCommand
        {
            Name = "  Daily Account  ",
            Institution = "Example Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "brl",
            OpeningBalance = -125.45m
        });

        Assert.True(result.Success);
        Assert.Equal(accountId, result.Data!.Id);
        Assert.Equal("Daily Account", result.Data.Name);
        Assert.Equal("Example Bank", result.Data.Institution);
        Assert.Equal(FinancialAccountType.Checking, result.Data.AccountType);
        Assert.Equal("BRL", result.Data.CurrencyCode);
        Assert.Equal(-125.45m, result.Data.OpeningBalance);
        Assert.Equal(Now, result.Data.CreatedAt);
        Assert.Equal(userId, store.Creation!.UserId);
        Assert.Equal("Daily Account", store.Creation.Name);
        Assert.Equal("BRL", store.Creation.CurrencyCode);
        Assert.Equal(Now, store.Creation.CreatedAt);
        Assert.Contains(FinancialAccountMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateLiveName_WhenCreated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubAccountStore(new FinancialAccountCreationResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.DuplicateName, result.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownCurrency_WhenCreated_ThenUnknownCodeIsNamed()
    {
        var subject = Guid.NewGuid();
        var store = new StubAccountStore(new FinancialAccountCreationResult(null, false));
        var command = ValidCommand();
        command.CurrencyCode = "zzz";

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store, ["BRL"])
            .HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(FinancialAccountMessages.UnknownCurrency("ZZZ"), result.Messages);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenNotFoundIsReturned()
    {
        var store = new StubAccountStore(new FinancialAccountCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, ["BRL"])
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenCreated_ThenNothingIsStored()
    {
        var store = new StubAccountStore(new FinancialAccountCreationResult(null, false));

        var result = await Handler(Guid.NewGuid(), null, store, []).HandleAsync(
            new CreateFinancialAccountCommand
            {
                Name = string.Empty,
                Institution = new string('i', 201),
                AccountType = (FinancialAccountType)99,
                CurrencyCode = string.Empty,
                OpeningBalance = 1234567890123456.12345m
            });

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.NameRequired, result.Errors);
        Assert.Contains(FinancialAccountMessages.InstitutionTooLong, result.Errors);
        Assert.Contains(FinancialAccountMessages.AccountTypeInvalid, result.Errors);
        Assert.Contains(FinancialAccountMessages.CurrencyRequired, result.Errors);
        Assert.Contains(FinancialAccountMessages.OpeningBalancePrecisionInvalid, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenCreated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubAccountStore(new FinancialAccountCreationResult(
            new FinancialAccountSnapshot(
                Guid.NewGuid(), userId, "Cash", null, FinancialAccountType.Cash,
                "BRL", 0, false, Now, Now),
            false));
        var handler = new CreateFinancialAccountCommandHandler(
            new CreateFinancialAccountCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            new StubCurrencyReader(["BRL"]),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static CreateFinancialAccountCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IFinancialAccountStore store,
        IReadOnlyCollection<string> currencies) => new(
            new CreateFinancialAccountCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            new StubCurrencyReader(currencies),
            store,
            new FixedTimeProvider(Now));

    private static CreateFinancialAccountCommand ValidCommand() => new()
    {
        Name = "Cash",
        AccountType = FinancialAccountType.Cash,
        CurrencyCode = "BRL",
        OpeningBalance = 0
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubAccountStore(FinancialAccountCreationResult result)
        : IFinancialAccountStore
    {
        public FinancialAccountCreation? Creation { get; private set; }

        public Task<FinancialAccountCreationResult> CreateAsync(
            FinancialAccountCreation creation,
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
