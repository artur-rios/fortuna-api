using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCounterpartiesQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyReader counterparties)
    : IQueryHandlerAsync<ListCounterpartiesQuery, CounterpartyListOutput>
{
    public async Task<DataOutput<CounterpartyListOutput?>> HandleAsync(
        ListCounterpartiesQuery query)
    {
        var profile = await CounterpartyQueryHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return DataOutput<CounterpartyListOutput?>.New.WithError(
                CounterpartyMessages.ProfileNotFound);
        }

        var snapshots = await counterparties.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        return DataOutput<CounterpartyListOutput?>.New
            .WithData(new CounterpartyListOutput
            {
                Counterparties = snapshots.Select(item => new CounterpartyOutput
                {
                    Id = item.Id,
                    Name = item.Name,
                    IsDeleted = item.IsDeleted,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                }).ToArray()
            })
            .WithMessage(CounterpartyMessages.ListedSuccessfully);
    }
}

public sealed class SuggestCounterpartyCategoryQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICounterpartyCategorySuggester counterparties)
    : IQueryHandlerAsync<SuggestCounterpartyCategoryQuery,
        CounterpartyCategorySuggestionOutput>
{
    public async Task<DataOutput<CounterpartyCategorySuggestionOutput?>> HandleAsync(
        SuggestCounterpartyCategoryQuery query)
    {
        var output = DataOutput<CounterpartyCategorySuggestionOutput?>.New;
        var profile = await CounterpartyQueryHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
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
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
