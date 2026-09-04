using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateCreditCardCommandHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [UnitFact]
    public async Task GivenValidDetails_WhenUpdated_ThenStoredCardIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var store = new StubCardUpdater(update => new CreditCardUpdateResult(
            Snapshot(cardId, userId, update), false));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(
            new UpdateCreditCardCommand
            {
                Id = cardId,
                Name = "  Travel  ",
                Issuer = "  New Bank  ",
                CreditLimit = 2500m,
                ClosingDay = 28,
                DueDay = 7
            });

        Assert.True(result.Success);
        Assert.Equal(cardId, result.Data?.Id);
        Assert.Equal("Travel", result.Data?.Name);
        Assert.Equal("New Bank", result.Data?.Issuer);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(2500m, result.Data?.CreditLimit);
        Assert.Equal("1234", result.Data?.LastFourDigits);
        Assert.Equal(CreatedAt, result.Data?.CreatedAt);
        Assert.Equal(UpdatedAt, result.Data?.UpdatedAt);
        Assert.Equal(userId, store.Update?.UserId);
        Assert.Contains(CreditCardMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateName_WhenUpdated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubCardUpdater(_ => new CreditCardUpdateResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.Contains(CreditCardMessages.DuplicateName, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingForeignOrDeletedCard_WhenUpdated_ThenNotFoundIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubCardUpdater(_ => new CreditCardUpdateResult(null, false));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.Contains(CreditCardMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenImmutableCurrency_WhenUpdated_ThenStorageIsNotCalled()
    {
        var store = new StubCardUpdater(_ => throw new InvalidOperationException());
        var command = ValidCommand();
        command.CurrencyCode = "USD";

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(command);

        Assert.Contains(CreditCardMessages.CurrencyImmutable, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenInvalidEditableFields_WhenUpdated_ThenEveryErrorIsReturned()
    {
        var store = new StubCardUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(
            new UpdateCreditCardCommand
            {
                Name = string.Empty,
                Issuer = new string('i', 201),
                CreditLimit = 0,
                ClosingDay = 0,
                DueDay = 32
            });

        Assert.Contains(CreditCardMessages.NameRequired, result.Errors);
        Assert.Contains(CreditCardMessages.IssuerTooLong, result.Errors);
        Assert.Contains(CreditCardMessages.CreditLimitPositive, result.Errors);
        Assert.Contains(CreditCardMessages.ClosingDayInvalid, result.Errors);
        Assert.Contains(CreditCardMessages.DueDayInvalid, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenNotFoundIsReturned()
    {
        var store = new StubCardUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(ValidCommand());

        Assert.Contains(CreditCardMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Update);
    }

    private static UpdateCreditCardCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        ICreditCardUpdater store) => new(
            new UpdateCreditCardCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(UpdatedAt));

    private static UpdateCreditCardCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Rewards",
        Issuer = "Example Bank",
        CreditLimit = 1000m,
        ClosingDay = 20,
        DueDay = 5
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id, subject, "Account Owner", "BRL", false, CreatedAt, CreatedAt);

    private static CreditCardSnapshot Snapshot(Guid id, Guid userId, CreditCardUpdate update) => new(
        id,
        userId,
        update.Name,
        update.Issuer,
        "BRL",
        update.CreditLimit,
        update.ClosingDay,
        update.DueDay,
        "1234",
        false,
        CreatedAt,
        UpdatedAt);

    private sealed class StubCardUpdater(
        Func<CreditCardUpdate, CreditCardUpdateResult> update) : ICreditCardUpdater
    {
        public CreditCardUpdate? Update { get; private set; }

        public Task<CreditCardUpdateResult> UpdateAsync(
            CreditCardUpdate value,
            CancellationToken cancellationToken)
        {
            Update = value;
            return Task.FromResult(update(value));
        }
    }

    private sealed class StubUserProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
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
