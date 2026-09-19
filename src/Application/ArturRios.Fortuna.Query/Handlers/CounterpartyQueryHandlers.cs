using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCounterpartiesQueryHandler(
    ICurrentProfileResolver profileResolver,
    ICounterpartyReader counterparties,
    PaginationOptions paginationOptions)
    : IQueryHandlerAsync<ListCounterpartiesQuery, CounterpartyListOutput>
{
    public async Task<DataOutput<CounterpartyListOutput?>> HandleAsync(
        ListCounterpartiesQuery query)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<CounterpartyListOutput?>.New.WithError(
                CounterpartyMessages.ProfileNotFound);
        }

        var page = new PageRequest(
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize));
        var snapshots = await counterparties.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            page,
            CancellationToken.None);

        return DataOutput<CounterpartyListOutput?>.New
            .WithData(new CounterpartyListOutput
            {
                Counterparties = snapshots.Items.Select(item => new CounterpartyOutput
                {
                    Id = item.Id,
                    Name = item.Name,
                    IsDeleted = item.IsDeleted,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                }).ToArray(),
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalItems = snapshots.TotalItems
            })
            .WithMessage(CounterpartyMessages.ListedSuccessfully);
    }
}

public sealed class SuggestCounterpartyCategoryQueryHandler(
    ICurrentProfileResolver profileResolver,
    ICounterpartyCategorySuggester counterparties)
    : IQueryHandlerAsync<SuggestCounterpartyCategoryQuery,
        CounterpartyCategorySuggestionOutput>
{
    public async Task<DataOutput<CounterpartyCategorySuggestionOutput?>> HandleAsync(
        SuggestCounterpartyCategoryQuery query)
    {
        var output = DataOutput<CounterpartyCategorySuggestionOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CounterpartyMessages.ProfileNotFound);
        }

        var result = await counterparties.SuggestCategoryAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (result.Outcome == CounterpartyCategorySuggestionOutcome.NotFound)
        {
            return output.WithError(CounterpartyMessages.NotFound);
        }

        return output
            .WithData(new CounterpartyCategorySuggestionOutput
            {
                CounterpartyId = result.CounterpartyId!.Value,
                HasSuggestion = result.CategoryId.HasValue,
                CategoryId = result.CategoryId,
                CategoryName = result.CategoryName
            })
            .WithMessage(result.CategoryId.HasValue
                ? CounterpartyMessages.SuggestedSuccessfully
                : CounterpartyMessages.NoSuggestion);
    }
}

internal static class CounterpartyQueryHandler
{
}
