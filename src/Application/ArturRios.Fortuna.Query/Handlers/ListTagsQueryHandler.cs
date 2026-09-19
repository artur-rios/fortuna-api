using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListTagsQueryHandler(
    IValidator<ListTagsQuery> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITagReader tags,
    PaginationOptions paginationOptions) : IQueryHandlerAsync<ListTagsQuery, TagListOutput>
{
    public async Task<DataOutput<TagListOutput?>> HandleAsync(ListTagsQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<TagListOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(
                    actor.SubjectId,
                    CancellationToken.None);
        if (profile is null)
        {
            return DataOutput<TagListOutput?>.New.WithError(TagMessages.ProfileNotFound);
        }

        var page = new PageRequest(
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize));
        var snapshots = await tags.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            page,
            CancellationToken.None);

        return DataOutput<TagListOutput?>.New
            .WithData(new TagListOutput
            {
                Tags = snapshots.Items.Select(tag => new TagOutput
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    IsDeleted = tag.IsDeleted,
                    CreatedAt = tag.CreatedAt,
                    UpdatedAt = tag.UpdatedAt
                }).ToArray(),
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalItems = snapshots.TotalItems
            })
            .WithMessage(TagMessages.ListedSuccessfully);
    }
}
