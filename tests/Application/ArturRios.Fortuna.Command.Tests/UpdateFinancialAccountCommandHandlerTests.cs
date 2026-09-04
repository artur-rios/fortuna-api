using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateFinancialAccountCommandHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [UnitFact]
    public async Task GivenValidDetails_WhenUpdated_ThenStoredAccountIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var store = new StubAccountUpdater(update => new FinancialAccountUpdateResult(
            Snapshot(accountId, userId, update.Name, update.Institution, update.AccountType),
            DuplicateName: false));
        var handler = Handler(subject, Profile(userId, subject), store);

        var result = await handler.HandleAsync(new UpdateFinancialAccountCommand
        {
            Id = accountId,
            Name = "  Reserve  ",
            Institution = "New Bank",
            AccountType = FinancialAccountType.Savings
        });

        Assert.True(result.Success);
        Assert.Equal(accountId, result.Data?.Id);
        Assert.Equal("Reserve", result.Data?.Name);
        Assert.Equal("New Bank", result.Data?.Institution);
        Assert.Equal(FinancialAccountType.Savings, result.Data?.AccountType);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(125, result.Data?.OpeningBalance);
        Assert.Equal(CreatedAt, result.Data?.CreatedAt);
        Assert.Equal(UpdatedAt, result.Data?.UpdatedAt);
        Assert.Equal(userId, store.Update?.UserId);
        Assert.Equal(UpdatedAt, store.Update?.UpdatedAt);
        Assert.Contains(FinancialAccountMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateName_WhenUpdated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubAccountUpdater(_ => new FinancialAccountUpdateResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.DuplicateName, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingOrForeignAccount_WhenUpdated_ThenNotFoundIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubAccountUpdater(_ => new FinancialAccountUpdateResult(null, false));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenImmutableFields_WhenUpdated_ThenEveryAttemptIsRejectedBeforeStorage()
    {
        var store = new StubAccountUpdater(_ => throw new InvalidOperationException());
        var command = ValidCommand();
        command.OwnerId = Guid.NewGuid();
        command.CurrencyCode = "USD";
        command.OpeningBalance = 999;

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.OwnerImmutable, result.Errors);
        Assert.Contains(FinancialAccountMessages.CurrencyImmutable, result.Errors);
        Assert.Contains(FinancialAccountMessages.OpeningBalanceImmutable, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenInvalidEditableFields_WhenUpdated_ThenEveryErrorIsReturned()
    {
        var store = new StubAccountUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(
            new UpdateFinancialAccountCommand
            {
                Name = string.Empty,
                Institution = new string('i', 201),
                AccountType = (FinancialAccountType)99
            });

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.NameRequired, result.Errors);
        Assert.Contains(FinancialAccountMessages.InstitutionTooLong, result.Errors);
        Assert.Contains(FinancialAccountMessages.AccountTypeInvalid, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenNotFoundIsReturned()
    {
        var store = new StubAccountUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenUpdated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubAccountUpdater(update => new FinancialAccountUpdateResult(
            Snapshot(update.Id, userId, update.Name, update.Institution, update.AccountType),
            false));
        var handler = new UpdateFinancialAccountCommandHandler(
            new UpdateFinancialAccountCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(UpdatedAt));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static UpdateFinancialAccountCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IFinancialAccountUpdater store) => new(
        new UpdateFinancialAccountCommandValidator(),
        new StubActorAccessor(new RequestActor(subject, 3, null, [])),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(UpdatedAt));

    private static UpdateFinancialAccountCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Cash",
        AccountType = FinancialAccountType.Cash
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        CreatedAt,
        CreatedAt);

    private static FinancialAccountSnapshot Snapshot(
        Guid id,
        Guid userId,
        string name,
        string? institution,
        FinancialAccountType accountType) => new(
        id,
        userId,
        name,
        institution,
        accountType,
        "BRL",
        125,
        false,
        CreatedAt,
        UpdatedAt);

    private sealed class StubAccountUpdater(
        Func<FinancialAccountUpdate, FinancialAccountUpdateResult> update) : IFinancialAccountUpdater
    {
        public FinancialAccountUpdate? Update { get; private set; }

        public Task<FinancialAccountUpdateResult> UpdateAsync(
            FinancialAccountUpdate value,
            CancellationToken cancellationToken)
        {
            Update = value;
            return Task.FromResult(update(value));
        }
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
