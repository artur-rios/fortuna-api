using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Investments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Investments;

public sealed class EfInvestmentStore(AppDbContext context) : IInvestmentStore
{
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
}
