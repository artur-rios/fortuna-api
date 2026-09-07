using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class DrillIntoAggregationQueryHandler(
    IValidator<DrillIntoAggregationQuery> validator,
    IUserProfileReader profiles,
    ITransactionDrillDownKeyCodec keyCodec,
    ITransactionReader transactions,
    ICategoryReader categories,
    IQueryHandlerAsync<AggregateTransactionsQuery, TransactionAggregationOutput> aggregationHandler,
    IRequestActorAccessor actorAccessor,
    PaginationOptions pagination,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<DrillIntoAggregationQuery, TransactionDrillDownOutput>
{
    public async Task<DataOutput<TransactionDrillDownOutput?>> HandleAsync(
        DrillIntoAggregationQuery query)
    {
        var output = DataOutput<TransactionDrillDownOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(TransactionDrillDownMessages.ProfileNotFound);
        }

        if (!keyCodec.TryDecode(query.Key, out var key) ||
            key is null ||
            !IsValid(key) ||
            key.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return output.WithError(TransactionDrillDownMessages.KeyInvalidOrExpired);
        }

        if (key.OwnerId != profile.Id)
        {
            return output.WithError(TransactionDrillDownMessages.BucketNotFound);
        }

        var selected = await SelectTransactionsAsync(profile.Id, key);
        var currentCount = await selected.CountAsync(CancellationToken.None);
        var changed = currentCount != key.RecordCount || await selected.AnyAsync(
            item => item.UpdatedAt > key.IssuedAt,
            CancellationToken.None);
        if (currentCount == 1)
        {
            var transaction = await selected.SingleAsync(CancellationToken.None);
            var direct = new TransactionDrillDownOutput
            {
                Mode = "transaction",
                SourceDimension = key.Dimension,
                MayDifferFromChart = changed,
                Transaction = TransactionProjection.Project(transaction),
                TotalItems = 1,
                PageNumber = 1,
                PageSize = 1
            };
            return Complete(output, direct, TransactionDrillDownMessages.TransactionRetrieved,
                changed);
        }

        var requestedDimension = string.IsNullOrWhiteSpace(query.Dimension)
            ? null
            : query.Dimension.Trim().ToLowerInvariant();
        if (requestedDimension is not null && requestedDimension != "period" &&
            key.Selections.Any(selection =>
                selection.Dimension == requestedDimension))
        {
            return output.WithError(TransactionDrillDownMessages.DimensionAlreadyUsed);
        }

        var target = Target(key, requestedDimension);
        if (target is not null && currentCount > 1)
        {
            var aggregate = await aggregationHandler.HandleAsync(AggregationQuery(key, target));
            if (!aggregate.Success || aggregate.Data is null)
            {
                return output.WithErrors(aggregate.Errors ?? []);
            }

            var detail = new TransactionDrillDownOutput
            {
                Mode = "aggregation",
                SourceDimension = key.Dimension,
                Dimension = target.Dimension,
                MayDifferFromChart = changed,
                Buckets = aggregate.Data.Buckets
            };
            return Complete(output, detail, TransactionDrillDownMessages.AggregationRetrieved,
                changed);
        }

        var pageSize = Math.Min(query.PageSize, pagination.MaximumPageSize);
        var page = await selected
            .OrderByDescending(item => item.OccurredOn)
            .ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .PaginateAsync(
                query.PageNumber,
                pageSize,
                orderBy: null,
                cancellationToken: CancellationToken.None);
        var list = new TransactionDrillDownOutput
        {
            Mode = "transactions",
            SourceDimension = key.Dimension,
            MayDifferFromChart = changed,
            Transactions = (page.Data ?? []).Select(TransactionProjection.Project).ToArray(),
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems
        };
        return Complete(output, list, TransactionDrillDownMessages.TransactionsRetrieved, changed);
    }

    private async Task<IQueryable<TransactionReadSnapshot>> SelectTransactionsAsync(
        Guid userId,
        TransactionDrillDownKeyPayload key)
    {
        var from = key.From;
        var to = key.To;
        foreach (var selection in key.Selections.Where(item => item.Dimension == "period"))
        {
            from = DateOnly.FromDayNumber(Math.Max(from.DayNumber, selection.From!.Value.DayNumber));
            to = DateOnly.FromDayNumber(Math.Min(to.DayNumber, selection.To!.Value.DayNumber));
        }

        IQueryable<TransactionReadSnapshot> selected = transactions.Query(new TransactionSearchCriteria
        {
            UserId = userId,
            From = from,
            To = to,
            FinancialAccountId = key.Filters.FinancialAccountId,
            CreditCardId = key.Filters.CreditCardId,
            CategoryId = key.Filters.CategoryId,
            TagId = key.Filters.TagId,
            RequiredTagIds = key.Selections
                .Where(item => item.Dimension == "tag")
                .Select(item => Guid.Parse(item.Value))
                .ToArray(),
            CounterpartyId = key.Filters.CounterpartyId,
            Direction = key.Filters.Direction,
            MinimumAmount = key.Filters.MinimumAmount,
            MaximumAmount = key.Filters.MaximumAmount,
            Text = key.Filters.Text,
            IncludeDeleted = false
        }).Where(item => !item.IsTransfer);

        var rollupCategoryIds = new Dictionary<string, Guid[]>();
        if (key.Selections.Any(item =>
                item.Dimension == "category" && item.RollupCategories))
        {
            var records = await categories.ListAsync(
                userId,
                includeDeleted: false,
                includeUsageCounts: false,
                CancellationToken.None);
            foreach (var selection in key.Selections.Where(item =>
                         item.Dimension == "category" && item.RollupCategories))
            {
                var root = Guid.Parse(selection.Value);
                var result = new HashSet<Guid>();
                var pending = new Stack<Guid>();
                pending.Push(root);
                while (pending.TryPop(out var current))
                {
                    if (!result.Add(current))
                    {
                        continue;
                    }

                    foreach (var child in records.Where(item => item.ParentId == current))
                    {
                        pending.Push(child.Id);
                    }
                }

                rollupCategoryIds[selection.Value] = [.. result];
            }
        }

        foreach (var selection in key.Selections.Where(item => item.Dimension != "period"))
        {
            selected = selection.Dimension switch
            {
                "account" => selected.Where(item =>
                    item.FinancialAccountId == Guid.Parse(selection.Value)),
                "card" => selected.Where(item =>
                    item.CreditCardId == Guid.Parse(selection.Value)),
                "category" when selection.RollupCategories => selected.Where(item =>
                    rollupCategoryIds[selection.Value].Contains(item.CategoryId)),
                "category" => selected.Where(item =>
                    item.CategoryId == Guid.Parse(selection.Value)),
                "counterparty" when selection.Value == "none" => selected.Where(item =>
                    item.CounterpartyId == null),
                "counterparty" => selected.Where(item =>
                    item.CounterpartyId == Guid.Parse(selection.Value)),
                "tag" => selected,
                _ => selected
            };
        }

        return selected;
    }

    private static TargetAggregation? Target(
        TransactionDrillDownKeyPayload key,
        string? requestedDimension)
    {
        if (requestedDimension is not null)
        {
            return new TargetAggregation(
                requestedDimension,
                requestedDimension == "period" ? FinerGranularity(key.Granularity) ?? "day" : null);
        }

        var finer = key.Dimension == "period" ? FinerGranularity(key.Granularity) : null;
        return finer is null ? null : new TargetAggregation("period", finer);
    }

    private static string? FinerGranularity(string? granularity) => granularity switch
    {
        "year" => "quarter",
        "quarter" => "month",
        "month" => "day",
        "week" => "day",
        _ => null
    };

    private static AggregateTransactionsQuery AggregationQuery(
        TransactionDrillDownKeyPayload key,
        TargetAggregation target) => new()
        {
            Dimension = target.Dimension,
            Granularity = target.Granularity,
            From = key.From,
            To = key.To,
            FinancialAccountId = key.Filters.FinancialAccountId,
            CreditCardId = key.Filters.CreditCardId,
            CategoryId = key.Filters.CategoryId,
            TagId = key.Filters.TagId,
            CounterpartyId = key.Filters.CounterpartyId,
            Direction = key.Filters.Direction,
            MinimumAmount = key.Filters.MinimumAmount,
            MaximumAmount = key.Filters.MaximumAmount,
            Text = key.Filters.Text,
            DisplayCurrencyCode = key.DisplayCurrencyCode,
            Selections = key.Selections
        };

    private static bool IsValid(TransactionDrillDownKeyPayload key) =>
        key.Version == 1 &&
        key.OwnerId != Guid.Empty &&
        key.From <= key.To &&
        key.IssuedAt < key.ExpiresAt &&
        key.RecordCount >= 0 &&
        !string.IsNullOrWhiteSpace(key.DisplayCurrencyCode) &&
        key.Filters is not null &&
        key.Selections is { Count: > 0 } &&
        AggregateTransactionsQueryValidator.SupportedDimensions.Contains(
            key.Dimension,
            StringComparer.OrdinalIgnoreCase) &&
        key.Selections.All(selection => selection is not null && IsValidSelection(selection));

    private static bool IsValidSelection(TransactionAggregationSelection selection) =>
        AggregateTransactionsQueryValidator.SupportedDimensions.Contains(
            selection.Dimension,
            StringComparer.OrdinalIgnoreCase) &&
        (selection.Dimension == "period"
            ? selection.From.HasValue && selection.To.HasValue && selection.From <= selection.To
            : (selection.Dimension == "counterparty" && selection.Value == "none") ||
              (Guid.TryParse(selection.Value, out var id) && id != Guid.Empty));

    private static DataOutput<TransactionDrillDownOutput?> Complete(
        DataOutput<TransactionDrillDownOutput?> output,
        TransactionDrillDownOutput data,
        string message,
        bool changed)
    {
        output.WithData(data).WithMessage(message);
        return changed ? output.WithMessage(TransactionDrillDownMessages.RecordsChanged) : output;
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private sealed record TargetAggregation(string Dimension, string? Granularity);
}
