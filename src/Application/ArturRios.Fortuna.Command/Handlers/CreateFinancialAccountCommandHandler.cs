using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateFinancialAccountCommandHandler(
    IValidator<CreateFinancialAccountCommand> validator,
    ICurrentProfileResolver profileResolver,
    ICurrencyReader currencies,
    IFinancialAccountStore accounts,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateFinancialAccountCommand, CreateFinancialAccountCommandOutput>
{
    public async Task<DataOutput<CreateFinancialAccountCommandOutput?>> HandleAsync(
        CreateFinancialAccountCommand command)
    {
        var output = DataOutput<CreateFinancialAccountCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var currencyCode = command.CurrencyCode.Trim().ToUpperInvariant();
        if (await currencies.FindByCodeAsync(currencyCode, CancellationToken.None) is null)
        {
            return output
                .WithError(FinancialAccountMessages.CurrencyNotSupported)
                .WithMessage(FinancialAccountMessages.UnknownCurrency(currencyCode));
        }

        var created = await accounts.CreateAsync(
            new FinancialAccountCreation(
                profile.Id,
                command.Name.Trim(),
                command.Institution,
                command.AccountType,
                currencyCode,
                command.OpeningBalance,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (created.DuplicateName)
        {
            return output.WithError(FinancialAccountMessages.DuplicateName);
        }

        if (created.Outcome == FinancialAccountCreationOutcome.ProfileNotFound)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        if (created.Outcome == FinancialAccountCreationOutcome.CurrencyNotSupported)
        {
            return output.WithError(FinancialAccountMessages.CurrencyNotSupported);
        }

        var account = created.Account!;

        return output
            .WithData(new CreateFinancialAccountCommandOutput
            {
                Id = account.Id,
                Name = account.Name,
                Institution = account.Institution,
                AccountType = account.AccountType,
                CurrencyCode = account.CurrencyCode,
                OpeningBalance = account.OpeningBalance,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt
            })
            .WithMessage(FinancialAccountMessages.CreatedSuccessfully);
    }
}
