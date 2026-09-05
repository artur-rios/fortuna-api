using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateInvestmentCommandHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [UnitFact]
    public async Task GivenValidDetails_WhenUpdated_ThenStoredInvestmentIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investmentId = Guid.NewGuid();
        var store = new StubInvestmentUpdater(update => new InvestmentUpdateResult(
            Snapshot(
                investmentId,
                userId,
                update.Instrument,
                update.Institution,
                update.InvestmentType),
            DuplicateInstrument: false));
        var handler = Handler(subject, Profile(userId, subject), store);

        var result = await handler.HandleAsync(new UpdateInvestmentCommand
        {
            Id = investmentId,
            Instrument = "  New Fund  ",
            Institution = "New Broker",
            InvestmentType = InvestmentType.Equity
        });

        Assert.True(result.Success);
        Assert.Equal(investmentId, result.Data?.Id);
        Assert.Equal("New Fund", result.Data?.Instrument);
        Assert.Equal("New Broker", result.Data?.Institution);
        Assert.Equal(InvestmentType.Equity, result.Data?.InvestmentType);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(CreatedAt, result.Data?.CreatedAt);
        Assert.Equal(UpdatedAt, result.Data?.UpdatedAt);
        Assert.Equal(userId, store.Update?.UserId);
        Assert.Equal(UpdatedAt, store.Update?.UpdatedAt);
        Assert.Contains(InvestmentMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDuplicateInstrument_WhenUpdated_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubInvestmentUpdater(_ => new InvestmentUpdateResult(null, true));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.Contains(InvestmentMessages.DuplicateInstrument, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingOrForeignInvestment_WhenUpdated_ThenNotFoundIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubInvestmentUpdater(_ => new InvestmentUpdateResult(null, false));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.Contains(InvestmentMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenCurrencyChange_WhenUpdated_ThenItIsRejectedBeforeStorage()
    {
        var store = new StubInvestmentUpdater(_ => throw new InvalidOperationException());
        var command = ValidCommand();
        command.CurrencyCode = "USD";

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(command);

        Assert.Contains(InvestmentMessages.CurrencyImmutable, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenInvalidEditableFields_WhenUpdated_ThenEveryErrorIsReturned()
    {
        var store = new StubInvestmentUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(
            new UpdateInvestmentCommand
            {
                Instrument = string.Empty,
                Institution = new string('i', 201),
                InvestmentType = (InvestmentType)99
            });

        Assert.Contains(InvestmentMessages.InstrumentRequired, result.Errors);
        Assert.Contains(InvestmentMessages.InstitutionTooLong, result.Errors);
        Assert.Contains(InvestmentMessages.InvestmentTypeInvalid, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenNotFoundIsReturned()
    {
        var store = new StubInvestmentUpdater(_ => throw new InvalidOperationException());

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(ValidCommand());

        Assert.Contains(InvestmentMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenUpdated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubProfileReader(Profile(userId, null));
        var store = new StubInvestmentUpdater(update => new InvestmentUpdateResult(
            Snapshot(
                update.Id,
                userId,
                update.Instrument,
                update.Institution,
                update.InvestmentType),
            false));
        var handler = new UpdateInvestmentCommandHandler(
            new UpdateInvestmentCommandValidator(),
            new StubActor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(UpdatedAt));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static UpdateInvestmentCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IInvestmentUpdater store) => new(
        new UpdateInvestmentCommandValidator(),
        new StubActor(new RequestActor(subject, 3, null, [])),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(UpdatedAt));

    private static UpdateInvestmentCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Instrument = "Fund",
        InvestmentType = InvestmentType.Fund
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Investment Owner",
        "BRL",
        false,
        CreatedAt,
        CreatedAt);

    private static InvestmentSnapshot Snapshot(
        Guid id,
        Guid userId,
        string instrument,
        string? institution,
        InvestmentType investmentType) => new(
        id,
        userId,
        instrument,
        institution,
        investmentType,
        "BRL",
        false,
        CreatedAt,
        UpdatedAt);

    private sealed class StubInvestmentUpdater(
        Func<InvestmentUpdate, InvestmentUpdateResult> update) : IInvestmentUpdater
    {
        public InvestmentUpdate? Update { get; private set; }

        public Task<InvestmentUpdateResult> UpdateAsync(
            InvestmentUpdate value,
            CancellationToken cancellationToken)
        {
            Update = value;
            return Task.FromResult(update(value));
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
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

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
