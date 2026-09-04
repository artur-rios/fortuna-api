using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateFinancialAccountCommandHandler(
    IValidator<CreateFinancialAccountCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
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

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
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
