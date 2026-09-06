using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListTagsQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITagReader tags) : IQueryHandlerAsync<ListTagsQuery, TagListOutput>
{
    public async Task<DataOutput<TagListOutput?>> HandleAsync(ListTagsQuery query)
    {
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

        var snapshots = await tags.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        return DataOutput<TagListOutput?>.New
            .WithData(new TagListOutput
            {
                Tags = snapshots.Select(tag => new TagOutput
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    IsDeleted = tag.IsDeleted,
                    CreatedAt = tag.CreatedAt,
                    UpdatedAt = tag.UpdatedAt
                }).ToArray()
            })
            .WithMessage(TagMessages.ListedSuccessfully);
    }
}
