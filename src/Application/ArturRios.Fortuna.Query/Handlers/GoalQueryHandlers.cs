using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListGoalsQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IGoalReader goals,
    TimeProvider timeProvider) : IQueryHandlerAsync<ListGoalsQuery, GoalListOutput>
{
    public async Task<DataOutput<GoalListOutput?>> HandleAsync(ListGoalsQuery query)
    {
        var profile = await GoalQueryHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<GoalListOutput?>.New.WithError(GoalMessages.ProfileNotFound);
        }

        var snapshots = await goals.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        return DataOutput<GoalListOutput?>.New.WithData(new GoalListOutput
        {
            Goals = snapshots.Select(GoalQueryHandler.ToOutput).ToArray()
        }).WithMessage(GoalMessages.ListedSuccessfully);
    }
}

public sealed class GetGoalByIdQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IGoalReader goals,
    TimeProvider timeProvider) : IQueryHandlerAsync<GetGoalByIdQuery, GoalOutput>
{
    public async Task<DataOutput<GoalOutput?>> HandleAsync(GetGoalByIdQuery query)
    {
        var output = DataOutput<GoalOutput?>.New;
        var profile = await GoalQueryHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(GoalMessages.ProfileNotFound);
        }

        var goal = await goals.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        return goal is null
            ? output.WithError(GoalMessages.NotFound)
            : output.WithData(GoalQueryHandler.ToOutput(goal))
                .WithMessage(GoalMessages.RetrievedSuccessfully);
    }
}

internal static class GoalQueryHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static GoalOutput ToOutput(GoalSnapshot goal) => new()
    {
        Id = goal.Id,
        Name = goal.Name,
        TargetAmount = goal.TargetAmount,
        CurrencyCode = goal.CurrencyCode,
        TargetDate = goal.TargetDate,
        Accounts = goal.Accounts.Select(item => new GoalResourceOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        Investments = goal.Investments.Select(item => new GoalResourceOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        CurrentProgress = new GoalProgressOutput
        {
            CurrentAmount = goal.CurrentProgress.CurrentAmount,
            Remaining = goal.CurrentProgress.Remaining,
            ProportionReached = goal.CurrentProgress.ProportionReached,
            IsReached = goal.CurrentProgress.IsReached,
            IsFullyConverted = goal.CurrentProgress.IsFullyConverted
        },
        IsDeleted = goal.IsDeleted,
        CreatedAt = goal.CreatedAt,
        UpdatedAt = goal.UpdatedAt
    };
}
