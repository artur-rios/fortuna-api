using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetFinancialAccountByIdQueryHandler(
    IValidator<GetFinancialAccountByIdQuery> validator,
    ICurrentProfileResolver profileResolver,
    IFinancialAccountReader accounts)
    : IQueryHandlerAsync<GetFinancialAccountByIdQuery, FinancialAccountOutput>
{
    public async Task<DataOutput<FinancialAccountOutput?>> HandleAsync(
        GetFinancialAccountByIdQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<FinancialAccountOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var output = DataOutput<FinancialAccountOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var account = await accounts.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        if (account is null)
        {
            return output.WithError(FinancialAccountMessages.NotFound);
        }

        return output
            .WithData(Project(account))
            .WithMessage(FinancialAccountMessages.RetrievedSuccessfully);
    }

    internal static FinancialAccountOutput Project(FinancialAccountSnapshot account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Institution = account.Institution,
        AccountType = account.AccountType,
        CurrencyCode = account.CurrencyCode,
        OpeningBalance = account.OpeningBalance,
        IsDeleted = account.IsDeleted,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt
    };
}
