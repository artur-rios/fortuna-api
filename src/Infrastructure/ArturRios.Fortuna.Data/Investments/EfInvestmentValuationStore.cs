using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Investments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Investments;

public sealed class EfInvestmentValuationStore(AppDbContext context)
    : IInvestmentValuationStore
{
    private const long ValuationLockNamespace = 0x49564C0000000000;

    public async Task<InvestmentValuationRecordResult> RecordAsync(
        InvestmentValuationRecord record,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var investment = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == record.InvestmentId &&
                item.User.PublicId == record.UserId &&
                !item.IsDeleted,
                cancellationToken);
        if (investment is null)
        {
            return Result(InvestmentValuationRecordOutcome.InvestmentNotFound);
        }

        var lockId = ValuationLockNamespace | (investment.Id & uint.MaxValue);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);
        var valuation = await context.InvestmentValuations.SingleOrDefaultAsync(item =>
            item.InvestmentId == investment.Id &&
            item.ValuedOn == record.ValuedOn &&
            !item.IsDeleted,
            cancellationToken);
        var replacedExisting = valuation is not null;
        if (valuation is null)
        {
            valuation = new InvestmentValuation(
                investment,
                record.Value,
                record.ValuedOn,
                record.RecordedAt);
            context.InvestmentValuations.Add(valuation);
        }
        else
        {
            valuation.ReplaceValue(record.Value, record.RecordedAt);
        }

        await context.SaveChangesAsync(cancellationToken);
        var movements = await context.InvestmentMovements
            .Where(item => item.InvestmentId == investment.Id)
            .ToArrayAsync(cancellationToken);
        var valuations = await context.InvestmentValuations
            .Where(item => item.InvestmentId == investment.Id)
            .ToArrayAsync(cancellationToken);
        var position = InvestmentPositionCalculator.Calculate(movements, valuations);
        await databaseTransaction.CommitAsync(cancellationToken);

        return Result(
            InvestmentValuationRecordOutcome.Succeeded,
            new InvestmentValuationSnapshot(
                valuation.PublicId,
                investment.PublicId,
                valuation.Value,
                investment.Currency.Code,
                valuation.ValuedOn,
                replacedExisting,
                position.Value,
                position.IsIndependentlyValued,
                position.ValuationValue,
                position.ValuedOn,
                valuation.CreatedAt,
                valuation.UpdatedAt));
    }

    private static InvestmentValuationRecordResult Result(
        InvestmentValuationRecordOutcome outcome,
        InvestmentValuationSnapshot? valuation = null) => new(valuation, outcome);
}
