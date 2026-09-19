using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCategoryByIdQueryHandler(
    IValidator<GetCategoryByIdQuery> validator,
    IUserProfileReader profiles,
    ICategoryReader categories,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetCategoryByIdQuery, CategoryOutput>
{
    public async Task<DataOutput<CategoryOutput?>> HandleAsync(GetCategoryByIdQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<CategoryOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var output = DataOutput<CategoryOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CategoryMessages.ProfileNotFound);
        }

        var records = await categories.ListSubtreeAsync(
            profile.Id,
            query.Id,
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
