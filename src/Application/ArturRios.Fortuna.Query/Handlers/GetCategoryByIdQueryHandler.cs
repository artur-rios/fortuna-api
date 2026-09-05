using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCategoryByIdQueryHandler(
    IUserProfileReader profiles,
    ICategoryReader categories,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetCategoryByIdQuery, CategoryOutput>
{
    public async Task<DataOutput<CategoryOutput?>> HandleAsync(GetCategoryByIdQuery query)
    {
        var output = DataOutput<CategoryOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CategoryMessages.ProfileNotFound);
        }

        var records = await categories.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            query.IncludeUsageCounts,
            CancellationToken.None);
        var category = CategoryTreeProjection.Find(records, query.Id, query.IncludeUsageCounts);
        if (category is null)
        {
            return output.WithError(CategoryMessages.NotFound);
        }

        return output
            .WithData(category)
            .WithMessage(CategoryMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
