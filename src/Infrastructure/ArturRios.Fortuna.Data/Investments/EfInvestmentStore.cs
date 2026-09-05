using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Shared.Investments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Investments;

public sealed class EfInvestmentStore(AppDbContext context)
    : IInvestmentStore, IInvestmentReader, IInvestmentUpdater, IInvestmentLifecycleStore
{
    public IQueryable<InvestmentPositionSnapshot> QueryPositions()
    {
        var liveValuations = context.InvestmentValuations.Where(item => !item.IsDeleted);
        var withLatestDate = context.Investments
            .AsNoTracking()
            .Select(investment => new
            {
                Investment = investment,
                LatestValuationDate = liveValuations
                    .Where(valuation => valuation.InvestmentId == investment.Id)
                    .Max(valuation => (DateOnly?)valuation.ValuedOn)
            });

        return withLatestDate.Select(item => new InvestmentPositionSnapshot
        {
            Id = item.Investment.PublicId,
            UserId = item.Investment.User.PublicId,
            Instrument = item.Investment.Instrument,
            Institution = item.Investment.Institution,
            InvestmentType = item.Investment.InvestmentType,
            CurrencyCode = item.Investment.Currency.Code,
            Position = (liveValuations
                .Where(valuation =>
                    valuation.InvestmentId == item.Investment.Id &&
                    valuation.ValuedOn == item.LatestValuationDate)
                .Select(valuation => (decimal?)valuation.Value)
                .FirstOrDefault() ?? 0m) +
                (context.InvestmentMovements
                    .Where(movement =>
                        movement.InvestmentId == item.Investment.Id &&
                        !movement.IsDeleted &&
                        movement.OccurredOn >
                            (item.LatestValuationDate ?? DateOnly.MinValue))
                    .Select(movement => (decimal?)(
                        movement.MovementType == InvestmentMovementType.Contribution ||
                        movement.MovementType == InvestmentMovementType.Yield
                            ? movement.Amount
                            : -movement.Amount))
                    .Sum() ?? 0m),
            IsIndependentlyValued = item.LatestValuationDate.HasValue,
            LatestValuationValue = liveValuations
                .Where(valuation =>
                    valuation.InvestmentId == item.Investment.Id &&
                    valuation.ValuedOn == item.LatestValuationDate)
                .Select(valuation => (decimal?)valuation.Value)
                .FirstOrDefault(),
            LatestValuationDate = item.LatestValuationDate,
            IsDeleted = item.Investment.IsDeleted,
            CreatedAt = item.Investment.CreatedAt,
            UpdatedAt = item.Investment.UpdatedAt
        });
    }

    public Task<InvestmentPositionSnapshot?> FindByIdWithPositionAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => QueryPositions().SingleOrDefaultAsync(
        investment =>
            investment.UserId == userId &&
            investment.Id == id &&
            !investment.IsDeleted,
        cancellationToken);

    public IQueryable<InvestmentValuationReadSnapshot> QueryValuations(
        Guid userId,
        Guid investmentId) => context.InvestmentValuations
        .AsNoTracking()
        .Where(valuation =>
            valuation.Investment.User.PublicId == userId &&
            valuation.Investment.PublicId == investmentId &&
            !valuation.Investment.IsDeleted &&
            !valuation.IsDeleted)
        .Select(valuation => new InvestmentValuationReadSnapshot
        {
            Id = valuation.PublicId,
            InvestmentId = valuation.Investment.PublicId,
            Value = valuation.Value,
            CurrencyCode = valuation.Investment.Currency.Code,
            ValuedOn = valuation.ValuedOn,
            CreatedAt = valuation.CreatedAt,
            UpdatedAt = valuation.UpdatedAt
        });

    public async Task<InvestmentCreationResult> CreateAsync(
        InvestmentCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            profile => profile.PublicId == creation.UserId,
            cancellationToken);
        var currency = await context.Currencies.SingleAsync(
            item => item.Code == creation.CurrencyCode,
            cancellationToken);
        var investment = new Investment(
            user,
            creation.Instrument,
            creation.Institution,
            creation.InvestmentType,
            currency,
            creation.CreatedAt);
        context.Investments.Add(investment);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: InvestmentMap.LiveInstrumentIndex
            })
        {
            context.Entry(investment).State = EntityState.Detached;
            return new InvestmentCreationResult(null, DuplicateInstrument: true);
        }

        return new InvestmentCreationResult(
            new InvestmentSnapshot(
                investment.PublicId,
                investment.User.PublicId,
                investment.Instrument,
                investment.Institution,
                investment.InvestmentType,
                investment.Currency.Code,
                investment.IsDeleted,
                investment.CreatedAt,
                investment.UpdatedAt),
            DuplicateInstrument: false);
    }

    public async Task<InvestmentUpdateResult> UpdateAsync(
        InvestmentUpdate update,
        CancellationToken cancellationToken)
    {
        var investment = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == update.UserId &&
                item.PublicId == update.Id &&
                !item.IsDeleted,
                cancellationToken);
        if (investment is null)
        {
            return new InvestmentUpdateResult(null, DuplicateInstrument: false);
        }

        investment.UpdateDetails(
            update.Instrument,
            update.Institution,
            update.InvestmentType,
            update.UpdatedAt);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: InvestmentMap.LiveInstrumentIndex
            })
        {
            context.Entry(investment).State = EntityState.Detached;
            return new InvestmentUpdateResult(null, DuplicateInstrument: true);
        }

        return new InvestmentUpdateResult(
            new InvestmentSnapshot(
                investment.PublicId,
                investment.User.PublicId,
                investment.Instrument,
                investment.Institution,
                investment.InvestmentType,
                investment.Currency.Code,
                investment.IsDeleted,
                investment.CreatedAt,
                investment.UpdatedAt),
            DuplicateInstrument: false);
    }

    public async Task<InvestmentLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var investment = await FindTrackedAsync(userId, id, cancellationToken);
        if (investment is null)
        {
            return LifecycleResult(InvestmentLifecycleOutcome.NotFound);
        }

        var deletion = investment.SoftDelete(changedAt);
        var movements = await context.InvestmentMovements
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        var valuations = await context.InvestmentValuations
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        foreach (var movement in movements)
        {
            movement.SoftDeleteFromCascade(deletion.CascadeId, changedAt);
        }

        foreach (var valuation in valuations)
        {
            valuation.SoftDeleteFromCascade(deletion.CascadeId, changedAt);
        }

        await context.SaveChangesAsync(cancellationToken);
        return LifecycleResult(InvestmentLifecycleOutcome.Succeeded, investment.PublicId);
    }

    public async Task<InvestmentLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var investment = await FindTrackedAsync(userId, id, cancellationToken);
        if (investment is null)
        {
            return LifecycleResult(InvestmentLifecycleOutcome.NotFound);
        }

        if (!investment.IsDeleted)
        {
            return LifecycleResult(InvestmentLifecycleOutcome.RestoreRequiresSoftDeletion);
        }

        var movements = await context.InvestmentMovements
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        var valuations = await context.InvestmentValuations
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        var cascadeId = investment.Restore(changedAt);
        foreach (var movement in movements)
        {
            movement.RestoreFromCascade(cascadeId, changedAt);
        }

        foreach (var valuation in valuations)
        {
            valuation.RestoreFromCascade(cascadeId, changedAt);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: InvestmentMap.LiveInstrumentIndex
            })
        {
            context.Entry(investment).State = EntityState.Detached;
            foreach (var movement in movements)
            {
                context.Entry(movement).State = EntityState.Detached;
            }

            foreach (var valuation in valuations)
            {
                context.Entry(valuation).State = EntityState.Detached;
            }

            return LifecycleResult(InvestmentLifecycleOutcome.DuplicateInstrument);
        }

        return LifecycleResult(InvestmentLifecycleOutcome.Succeeded, investment.PublicId);
    }

    public async Task<InvestmentLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var investment = await FindTrackedAsync(userId, id, cancellationToken);
        if (investment is null)
        {
            return LifecycleResult(InvestmentLifecycleOutcome.NotFound);
        }

        try
        {
            investment.EnsureHardDeletionAllowed();
        }
        catch (RecordLifecycleConflictException exception) when (
            exception.Conflict == RecordLifecycleConflict.HardDeleteRequiresSoftDeletion)
        {
            return LifecycleResult(InvestmentLifecycleOutcome.HardDeleteRequiresSoftDeletion);
        }

        var movements = await context.InvestmentMovements
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        var valuations = await context.InvestmentValuations
            .Where(item => item.InvestmentId == investment.Id)
            .ToListAsync(cancellationToken);
        context.InvestmentMovements.RemoveRange(movements);
        context.InvestmentValuations.RemoveRange(valuations);
        context.Investments.Remove(investment);
        await context.SaveChangesAsync(cancellationToken);
        return LifecycleResult(InvestmentLifecycleOutcome.Succeeded, investment.PublicId);
    }

    private Task<Investment?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.Investments.SingleOrDefaultAsync(item =>
        item.User.PublicId == userId && item.PublicId == id,
        cancellationToken);

    private static InvestmentLifecycleResult LifecycleResult(
        InvestmentLifecycleOutcome outcome,
        Guid? id = null,
        string? referencingGoal = null) => new(id, outcome, referencingGoal);
}
