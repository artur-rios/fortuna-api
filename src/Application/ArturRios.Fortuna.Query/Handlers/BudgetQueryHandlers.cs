using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListBudgetsQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetReader budgets,
    TimeProvider timeProvider) : IQueryHandlerAsync<ListBudgetsQuery, BudgetListOutput>
{
    public async Task<DataOutput<BudgetListOutput?>> HandleAsync(ListBudgetsQuery query)
    {
        var profile = await BudgetQueryHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<BudgetListOutput?>.New.WithError(BudgetMessages.ProfileNotFound);
        }

        var snapshots = await budgets.ListAsync(
            profile.Id,
            query.IncludeDeleted,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        return DataOutput<BudgetListOutput?>.New
            .WithData(new BudgetListOutput
            {
                Budgets = snapshots.Select(BudgetQueryHandler.ToOutput).ToArray()
            })
            .WithMessage(BudgetMessages.ListedSuccessfully);
    }
}

public sealed class GetBudgetByIdQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetReader budgets,
    TimeProvider timeProvider) : IQueryHandlerAsync<GetBudgetByIdQuery, BudgetOutput>
{
    public async Task<DataOutput<BudgetOutput?>> HandleAsync(GetBudgetByIdQuery query)
    {
        var output = DataOutput<BudgetOutput?>.New;
        var profile = await BudgetQueryHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(BudgetMessages.ProfileNotFound);
        }

        var snapshot = await budgets.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        return snapshot is null
            ? output.WithError(BudgetMessages.NotFound)
            : output
                .WithData(BudgetQueryHandler.ToOutput(snapshot))
                .WithMessage(BudgetMessages.RetrievedSuccessfully);
    }
}

public sealed class GetBudgetConsumptionQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IBudgetConsumptionReader budgets,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetBudgetConsumptionQuery, BudgetConsumptionDetailOutput>
{
    public async Task<DataOutput<BudgetConsumptionDetailOutput?>> HandleAsync(
        GetBudgetConsumptionQuery query)
    {
        var output = DataOutput<BudgetConsumptionDetailOutput?>.New;
        var profile = await BudgetQueryHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return output.WithError(BudgetMessages.ProfileNotFound);
        }

        var periodDate = query.PeriodStart ??
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var result = await budgets.GetConsumptionAsync(
            profile.Id,
            query.Id,
            periodDate,
            CancellationToken.None);
        if (result.Outcome == BudgetConsumptionOutcome.NotFound)
        {
            return output.WithError(BudgetMessages.NotFound);
        }

        var consumption = result.Consumption!;
        var response = output
            .WithData(BudgetQueryHandler.ToOutput(
                consumption,
                result.Outcome == BudgetConsumptionOutcome.PeriodPrecedesBudget
                    ? BudgetMessages.PeriodPrecedesBudget
                    : null))
            .WithMessage(result.Outcome == BudgetConsumptionOutcome.PeriodPrecedesBudget
                ? BudgetMessages.PeriodPrecedesBudget
                : BudgetMessages.ConsumptionRetrievedSuccessfully);
        return consumption.IsFullyConverted
            ? response
            : response.WithMessage(FigureConversionMessages.PartiallyConverted);
    }
}

internal static class BudgetQueryHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static BudgetOutput ToOutput(BudgetSnapshot budget) => new()
    {
        Id = budget.Id,
        Amount = budget.Amount,
        CurrencyCode = budget.CurrencyCode,
        PeriodType = budget.PeriodType,
        PeriodStart = budget.PeriodStart,
        IncludeDescendants = budget.IncludeDescendants,
        Categories = budget.Categories.Select(item => new BudgetCategoryOutput
        {
            Id = item.Id,
            Name = item.Name
        }).ToArray(),
        CurrentPeriod = new BudgetConsumptionOutput
        {
            PeriodStart = budget.CurrentPeriod.PeriodStart,
            PeriodEnd = budget.CurrentPeriod.PeriodEnd,
            Spent = budget.CurrentPeriod.Spent,
            Remaining = budget.CurrentPeriod.Remaining,
            IsExceeded = budget.CurrentPeriod.IsExceeded,
            Overage = budget.CurrentPeriod.Overage,
            IsFullyConverted = budget.CurrentPeriod.IsFullyConverted
        },
        IsDeleted = budget.IsDeleted,
        CreatedAt = budget.CreatedAt,
        UpdatedAt = budget.UpdatedAt
    };

    public static BudgetConsumptionDetailOutput ToOutput(
        BudgetConsumptionDetailSnapshot consumption,
        string? reason) => new()
        {
            BudgetId = consumption.BudgetId,
            BudgetAmount = consumption.BudgetAmount,
            CurrencyCode = consumption.CurrencyCode,
            RequestedDate = consumption.RequestedDate,
            PeriodStart = consumption.PeriodStart,
            PeriodEnd = consumption.PeriodEnd,
            Spent = consumption.Spent,
            Remaining = consumption.Remaining,
            IsExceeded = consumption.IsExceeded,
            Overage = consumption.Overage,
            IsCovered = consumption.IsCovered,
            IsFullyConverted = consumption.IsFullyConverted,
            Reason = reason,
            Conversions = consumption.Conversions.Select(item => new BudgetConversionOutput
            {
                SourceCurrencyCode = item.SourceCurrencyCode,
                SourceAmount = item.SourceAmount,
                ConvertedAmount = item.ConvertedAmount,
                AppliedRate = item.AppliedRate,
                RateDate = item.RateDate,
                RateSource = item.RateSource,
                UnconvertedReason = item.UnconvertedReason
            }).ToArray()
        };
}
