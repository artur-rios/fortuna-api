using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class InstallmentPlanQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedPlan_WhenRead_ThenPlanAndInstallmentsAreMapped()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var reader = new StubReader(snapshot);
        var query = new GetInstallmentPlanByIdQuery
        {
            Id = snapshot.Id,
            IncludeDeleted = true
        };

        var result = await Handler(profile, reader).HandleAsync(query);

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.OriginalTotalAmount, result.Data?.OriginalTotalAmount);
        Assert.Equal(snapshot.Installments.Single().TransactionId,
            result.Data?.Installments.Single().TransactionId);
        Assert.Equal(profile.Id, reader.UserId);
        Assert.True(reader.IncludeDeleted);
        Assert.Contains(InstallmentPlanMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingPlan_WhenRead_ThenNotFoundIsReturned()
    {
        var result = await Handler(Profile(), new StubReader(null)).HandleAsync(
            new GetInstallmentPlanByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(InstallmentPlanMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRead_ThenReaderIsNotCalled()
    {
        var reader = new StubReader(Snapshot());

        var result = await Handler(null, reader).HandleAsync(
            new GetInstallmentPlanByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(InstallmentPlanMessages.ProfileNotFound, result.Errors);
        Assert.Null(reader.UserId);
    }

    [UnitFact]
    public async Task GivenEmptyId_WhenRead_ThenValidationErrorIsReturned()
    {
        var reader = new StubReader(Snapshot());

        var result = await Handler(Profile(), reader).HandleAsync(
            new GetInstallmentPlanByIdQuery());

        Assert.Contains(InstallmentPlanMessages.IdRequired, result.Errors);
        Assert.Null(reader.UserId);
    }

    private static GetInstallmentPlanByIdQueryHandler Handler(
        UserProfileSnapshot? profile,
        IInstallmentPlanReader reader) => new(
        new GetInstallmentPlanByIdQueryValidator(),
        new StubProfileReader(profile),
        reader,
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])));

    private static InstallmentPlanSnapshot Snapshot() => new()
    {
        Id = Guid.NewGuid(),
        CreditCardId = Guid.NewGuid(),
        TotalAmount = 50m,
        CurrencyCode = "BRL",
        OriginalTotalAmount = 10m,
        OriginalCurrencyCode = "USD",
        AppliedRate = 5m,
        RateDate = new DateOnly(2026, 9, 4),
        InstallmentCount = 2,
        PurchasedOn = new DateOnly(2026, 9, 5),
        CreatedAt = Now,
        UpdatedAt = Now,
        Installments =
        [
            new InstallmentSnapshot(
                Guid.NewGuid(), 1, 25m, "BRL", 5m, "USD", 5m,
                new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 5),
                Guid.NewGuid(), false, false)
        ]
    };

    private sealed class StubReader(InstallmentPlanSnapshot? snapshot) : IInstallmentPlanReader
    {
        public Guid? UserId { get; private set; }
        public bool IncludeDeleted { get; private set; }

        public Task<InstallmentPlanSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            return Task.FromResult(snapshot);
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

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
}
