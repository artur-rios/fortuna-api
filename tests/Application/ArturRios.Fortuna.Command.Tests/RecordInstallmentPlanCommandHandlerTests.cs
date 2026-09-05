using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordInstallmentPlanCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedCard_WhenRecorded_ThenPlanAndInstallmentsAreReturned()
    {
        var profile = Profile();
        var command = Command();
        var snapshot = Snapshot(command);
        var store = new StubStore(new(snapshot, InstallmentPlanRecordOutcome.Succeeded));

        var result = await Handler(profile, store).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.TotalAmount, result.Data?.TotalAmount);
        Assert.Equal(3, result.Data?.Installments.Count);
        Assert.Equal(profile.Id, store.Record?.UserId);
        Assert.Equal(Now, store.Record?.CreatedAt);
        Assert.Contains(InstallmentPlanMessages.RecordedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(InstallmentPlanRecordOutcome.CreditCardNotFound,
        InstallmentPlanMessages.CreditCardNotFound)]
    [InlineData(InstallmentPlanRecordOutcome.CategoryNotFound,
        InstallmentPlanMessages.CategoryNotFound)]
    [InlineData(InstallmentPlanRecordOutcome.CurrencyNotSupported,
        InstallmentPlanMessages.CurrencyNotSupported)]
    [InlineData(InstallmentPlanRecordOutcome.ExchangeRateUnavailable,
        InstallmentPlanMessages.ExchangeRateUnavailable)]
    [InlineData(InstallmentPlanRecordOutcome.AmountTooSmall,
        InstallmentPlanMessages.AmountTooSmall)]
    public async Task GivenStoreRefusal_WhenHandled_ThenCanonicalErrorIsReturned(
        InstallmentPlanRecordOutcome outcome,
        string expected)
    {
        var result = await Handler(
            Profile(),
            new StubStore(new(null, outcome))).HandleAsync(Command());

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenHandled_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new(Snapshot(Command()),
            InstallmentPlanRecordOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(Command());

        Assert.Contains(InstallmentPlanMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenInvalidCommand_WhenHandled_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new(Snapshot(Command()),
            InstallmentPlanRecordOutcome.Succeeded));
        var command = Command();
        command.InstallmentCount = 1;

        var result = await Handler(Profile(), store).HandleAsync(command);

        Assert.Contains(InstallmentPlanMessages.InstallmentCountMinimum, result.Errors);
        Assert.Null(store.Record);
    }

    private static RecordInstallmentPlanCommandHandler Handler(
        UserProfileSnapshot? profile,
        IInstallmentPlanStore store) => new(
        new RecordInstallmentPlanCommandValidator(new FixedTimeProvider(Now)),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RecordInstallmentPlanCommand Command() => new()
    {
        CreditCardId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        TotalAmount = 100m,
        InstallmentCount = 3,
        PurchasedOn = new DateOnly(2026, 9, 5),
        CurrencyCode = "USD",
        Counterparty = "Shop"
    };

    private static InstallmentPlanSnapshot Snapshot(
        RecordInstallmentPlanCommand command) => new()
        {
            Id = Guid.NewGuid(),
            CreditCardId = command.CreditCardId,
            TotalAmount = 500m,
            CurrencyCode = "BRL",
            OriginalTotalAmount = command.TotalAmount,
            OriginalCurrencyCode = "USD",
            AppliedRate = 5m,
            RateDate = command.PurchasedOn,
            InstallmentCount = command.InstallmentCount,
            PurchasedOn = command.PurchasedOn,
            CreatedAt = Now,
            UpdatedAt = Now,
            Installments = Enumerable.Range(1, command.InstallmentCount)
            .Select(number => new InstallmentSnapshot(
                Guid.NewGuid(),
                (short)number,
                number == 1 ? 166.68m : 166.66m,
                "BRL",
                number == 1 ? 33.34m : 33.33m,
                "USD",
                5m,
                command.PurchasedOn,
                command.PurchasedOn.AddMonths(number - 1),
                Guid.NewGuid(),
                false,
                false))
            .ToArray()
        };

    private sealed class StubStore(InstallmentPlanRecordResult result) : IInstallmentPlanStore
    {
        public InstallmentPlanRecord? Record { get; private set; }

        public Task<InstallmentPlanRecordResult> RecordAsync(
            InstallmentPlanRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
}
