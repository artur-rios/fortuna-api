using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCategoryTreeQueryHandler(
    IUserProfileReader profiles,
    ICategoryReader categories,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetCategoryTreeQuery, CategoryTreeOutput>
{
    public async Task<DataOutput<CategoryTreeOutput?>> HandleAsync(GetCategoryTreeQuery query)
    {
        var output = DataOutput<CategoryTreeOutput?>.New;
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
        var tree = CategoryTreeProjection.Build(records, query.IncludeUsageCounts);
        var result = output
            .WithData(new CategoryTreeOutput
            {
                Categories = tree,
                CanSeedDefaults = tree.Count == 0
            })
            .WithMessage(CategoryMessages.TreeRetrievedSuccessfully);

        return tree.Count == 0
            ? result.WithMessage(CategoryMessages.DefaultSetAvailable)
            : result;
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
