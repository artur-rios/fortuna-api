using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class TransferQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTransfer_WhenRead_ThenBothLegsAreMapped()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var reader = new StubReader(snapshot);

        var result = await Handler(profile, reader).HandleAsync(
            new GetTransferByIdQuery { Id = snapshot.Id });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.OutboundTransactionId, result.Data?.OutboundTransactionId);
        Assert.Equal(snapshot.InboundTransactionId, result.Data?.InboundTransactionId);
        Assert.Equal(snapshot.DestinationFinancialAccountId,
            result.Data?.DestinationFinancialAccountId);
        Assert.Equal(profile.Id, reader.UserId);
        Assert.Contains(TransferMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedTransferRequested_WhenRead_ThenIncludeDeletedIsForwarded()
    {
        var snapshot = Snapshot();
        var reader = new StubReader(snapshot);

        var result = await Handler(Profile(), reader).HandleAsync(
            new GetTransferByIdQuery
            {
                Id = snapshot.Id,
                IncludeDeleted = true
            });

        Assert.True(result.Success);
        Assert.True(reader.IncludeDeleted);
    }

    [UnitFact]
    public async Task GivenForeignOrMissingTransfer_WhenRead_ThenNotFoundIsReturned()
    {
        var result = await Handler(Profile(), new StubReader(null)).HandleAsync(
            new GetTransferByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(TransferMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRead_ThenReaderIsNotCalled()
    {
        var reader = new StubReader(Snapshot());

        var result = await Handler(null, reader).HandleAsync(
            new GetTransferByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(TransferMessages.ProfileNotFound, result.Errors);
        Assert.False(reader.Called);
    }

    [UnitFact]
    public async Task GivenEmptyId_WhenRead_ThenValidationPreventsLookup()
    {
        var reader = new StubReader(Snapshot());

        var result = await Handler(Profile(), reader).HandleAsync(new GetTransferByIdQuery());

        Assert.Contains(TransferMessages.TransferIdRequired, result.Errors);
        Assert.False(reader.Called);
    }

    private static GetTransferByIdQueryHandler Handler(
        UserProfileSnapshot? profile,
        ITransferReader reader) => new(
        new GetTransferByIdQueryValidator(),
        new StubProfileReader(profile),
        reader,
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])));

    private static TransferReadSnapshot Snapshot() => new()
    {
        Id = Guid.NewGuid(),
        OutboundTransactionId = Guid.NewGuid(),
        InboundTransactionId = Guid.NewGuid(),
        OriginFinancialAccountId = Guid.NewGuid(),
        DestinationFinancialAccountId = Guid.NewGuid(),
        OutboundAmount = 10m,
        OutboundCurrencyCode = "USD",
        InboundAmount = 50m,
        InboundCurrencyCode = "BRL",
        AppliedRate = 5m,
        RateDate = new DateOnly(2026, 9, 4),
        OccurredOn = new DateOnly(2026, 9, 5),
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubReader(TransferReadSnapshot? snapshot) : ITransferReader
    {
        public bool Called { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IncludeDeleted { get; private set; }

        public Task<TransferReadSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            Called = true;
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
}
