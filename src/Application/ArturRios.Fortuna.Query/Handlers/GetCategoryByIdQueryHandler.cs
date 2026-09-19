using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCategoryByIdQueryHandler(
    ICurrentProfileResolver profileResolver,
    ICategoryReader categories)
    : IQueryHandlerAsync<GetCategoryByIdQuery, CategoryOutput>
{
    public async Task<DataOutput<CategoryOutput?>> HandleAsync(GetCategoryByIdQuery query)
    {
        var output = DataOutput<CategoryOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
}
