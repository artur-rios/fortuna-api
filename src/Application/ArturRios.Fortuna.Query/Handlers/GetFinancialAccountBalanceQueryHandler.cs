using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetFinancialAccountBalanceQueryHandler(
    IValidator<GetFinancialAccountBalanceQuery> validator,
    ICurrentProfileResolver profileResolver,
    IFinancialAccountReader accounts,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetFinancialAccountBalanceQuery, FinancialAccountBalanceOutput>
{
    public async Task<DataOutput<FinancialAccountBalanceOutput?>> HandleAsync(
        GetFinancialAccountBalanceQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<FinancialAccountBalanceOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var output = DataOutput<FinancialAccountBalanceOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var asOf = query.AsOf ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var balance = await accounts.CalculateBalanceAsync(
            profile.Id,
            query.Id,
            asOf,
            CancellationToken.None);
        if (balance is null)
        {
            return output.WithError(FinancialAccountMessages.NotFound);
        }

        return output
            .WithData(new FinancialAccountBalanceOutput
            {
                Id = balance.Id,
                CurrencyCode = balance.CurrencyCode,
                Balance = balance.Balance,
                AsOf = balance.AsOf
            })
            .WithMessage(FinancialAccountMessages.BalanceRetrievedSuccessfully);
    }
}
