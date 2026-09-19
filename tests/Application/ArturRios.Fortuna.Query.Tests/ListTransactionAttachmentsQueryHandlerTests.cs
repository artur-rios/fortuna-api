using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ListTransactionAttachmentsQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TransactionId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [UnitFact]
    public async Task GivenOwnedTransaction_WhenListed_ThenItsLiveAttachmentsAreReturnedOldestFirst()
    {
        // Given
        var profile = Profile();
        var first = Attachment("receipt.pdf", Now);
        var second = Attachment("invoice.pdf", Now.AddHours(1));
        var reader = new StubReader(owned: true, first, second);

        // When
        var result = await Handler(profile, reader).HandleAsync(Query());

        // Then
        Assert.True(result.Success);
        Assert.Equal(["receipt.pdf", "invoice.pdf"], result.Data!.Select(item => item.FileName));
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(TransactionId, result.Data!.First().TransactionId);
        Assert.Equal("application/pdf", result.Data!.First().ContentType);
        Assert.Equal(1_024, result.Data!.First().SizeInBytes);
        Assert.Contains(AttachmentMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedAttachment_WhenListed_ThenItIsAbsentUnlessExplicitlyIncluded()
    {
        // Given
        var profile = Profile();
        var live = Attachment("live.pdf", Now);
        var deleted = Attachment("archived.pdf", Now.AddHours(1), isDeleted: true);
        var reader = new StubReader(owned: true, live, deleted);
        var handler = Handler(profile, reader);

        // When
        var byDefault = await handler.HandleAsync(Query());
        var included = await handler.HandleAsync(Query(includeDeleted: true));

        // Then
        Assert.Equal("live.pdf", Assert.Single(byDefault.Data!).FileName);
        Assert.Equal(2, included.TotalItems);
        Assert.True(included.Data!.Single(item => item.Id == deleted.Id).IsDeleted);
        Assert.False(included.Data!.Single(item => item.Id == live.Id).IsDeleted);
    }

    [UnitFact]
    public async Task GivenTransactionOfAnotherUser_WhenListed_ThenNotFoundIsReturnedWithoutQuerying()
    {
        // Given
        var reader = new StubReader(owned: false, Attachment("private.pdf", Now));

        // When
        var result = await Handler(Profile(), reader).HandleAsync(Query());

        // Then
        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.TransactionNotFound, result.Errors);
        Assert.False(reader.Queried);
    }

    [UnitFact]
    public async Task GivenTransactionWithNoAttachments_WhenListed_ThenAnEmptyPageSucceeds()
    {
        // Given
        var reader = new StubReader(owned: true);

        // When
        var result = await Handler(Profile(), reader).HandleAsync(Query());

        // Then
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal(0, result.TotalItems);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenListed_ThenNotFoundIsReturned()
    {
        // Given
        var reader = new StubReader(owned: true, Attachment("receipt.pdf", Now));

        // When
        var result = await Handler(null, reader).HandleAsync(Query());

        // Then
        Assert.Contains(AttachmentMessages.ProfileNotFound, result.Errors);
        Assert.False(reader.Queried);
    }

    [UnitFact]
    public async Task GivenInvalidPaging_WhenListed_ThenFieldsAreNamedInErrors()
    {
        // Given
        var reader = new StubReader(owned: true);

        // When
        var result = await Handler(Profile(), reader).HandleAsync(
            new ListTransactionAttachmentsQuery
            {
                TransactionId = TransactionId,
                PageNumber = 0,
                PageSize = 0
            });

        // Then
        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(AttachmentMessages.InvalidPageSize, result.Errors);
        Assert.False(reader.Queried);
    }

    [UnitFact]
    public async Task GivenPageSizeAboveTheMaximum_WhenListed_ThenItIsCapped()
    {
        // Given
        var reader = new StubReader(
            owned: true,
            Attachment("one.pdf", Now),
            Attachment("two.pdf", Now.AddHours(1)),
            Attachment("three.pdf", Now.AddHours(2)));

        // When
        var result = await Handler(Profile(), reader, maximumPageSize: 2).HandleAsync(
            new ListTransactionAttachmentsQuery
            {
                TransactionId = TransactionId,
                PageSize = 500
            });

        // Then
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(3, result.TotalItems);
    }

    private static IPaginatedQueryHandlerAsync<ListTransactionAttachmentsQuery, AttachmentOutput> Handler(
        UserProfileSnapshot? profile,
        IAttachmentMetadataReader metadata,
        int maximumPageSize = 100) => new ListTransactionAttachmentsQueryHandler(
        new CurrentProfileResolver(new StubActorAccessor(
            new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])), new StubProfileReader(profile)),
        metadata,
        new PaginationOptions(maximumPageSize)).Validated(new ListTransactionAttachmentsQueryValidator());

    private static ListTransactionAttachmentsQuery Query(bool includeDeleted = false) => new()
    {
        TransactionId = TransactionId,
        IncludeDeleted = includeDeleted
    };

    private static AttachmentListSnapshot Attachment(
        string fileName,
        DateTimeOffset createdAt,
        bool isDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = TransactionId,
            FileName = fileName,
            ContentType = "application/pdf",
            SizeInBytes = 1_024,
            IsDeleted = isDeleted,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Attachment Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubReader(bool owned, params AttachmentListSnapshot[] attachments)
        : IAttachmentMetadataReader
    {
        public bool Queried { get; private set; }

        public Task<AttachmentReadSnapshot?> FindOwnedAsync(
            Guid userId,
            Guid attachmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AttachmentReadSnapshot?>(null);

        public Task<bool> IsOwnedTransactionAsync(
            Guid userId,
            Guid transactionId,
            CancellationToken cancellationToken) => Task.FromResult(owned);

        public IQueryable<AttachmentListSnapshot> QueryForTransaction(
            Guid userId,
            Guid transactionId)
        {
            Queried = true;

            return attachments.AsQueryable();
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

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
