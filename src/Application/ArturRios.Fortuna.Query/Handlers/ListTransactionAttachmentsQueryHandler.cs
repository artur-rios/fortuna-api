using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListTransactionAttachmentsQueryHandler(
    IValidator<ListTransactionAttachmentsQuery> validator,
    IUserProfileReader profiles,
    IAttachmentMetadataReader metadata,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListTransactionAttachmentsQuery, AttachmentOutput>
{
    public async Task<PaginatedOutput<AttachmentOutput>> HandleAsync(
        ListTransactionAttachmentsQuery query)
    {
        var output = PaginatedOutput<AttachmentOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(AttachmentMessages.ProfileNotFound);
        }

        // A transaction that is not the acting user's is indistinguishable from one that
        // does not exist, per FR-AT-08.
        var owned = await metadata.IsOwnedTransactionAsync(
            profile.Id,
            query.TransactionId,
            CancellationToken.None);
        if (!owned)
        {
            return output.WithError(AttachmentMessages.TransactionNotFound);
        }

        var filtered = metadata.QueryForTransaction(profile.Id, query.TransactionId);
        if (!query.IncludeDeleted)
        {
            filtered = filtered.Where(attachment => !attachment.IsDeleted);
        }

        var projected = filtered
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .Select(attachment => new AttachmentOutput
            {
                Id = attachment.Id,
                TransactionId = attachment.TransactionId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeInBytes = attachment.SizeInBytes,
                IsDeleted = attachment.IsDeleted,
                CreatedAt = attachment.CreatedAt,
                UpdatedAt = attachment.UpdatedAt
            });
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return page.WithMessage(AttachmentMessages.ListedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
