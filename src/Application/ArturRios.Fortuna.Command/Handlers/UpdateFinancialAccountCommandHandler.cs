using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateFinancialAccountCommandHandler(
    IValidator<UpdateFinancialAccountCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IFinancialAccountUpdater accounts,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateFinancialAccountCommand, UpdateFinancialAccountCommandOutput>
{
    public async Task<DataOutput<UpdateFinancialAccountCommandOutput?>> HandleAsync(
        UpdateFinancialAccountCommand command)
    {
        var output = DataOutput<UpdateFinancialAccountCommandOutput?>.New;
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

        var updated = await accounts.UpdateAsync(
            new FinancialAccountUpdate(
                profile.Id,
                command.Id,
                command.Name.Trim(),
                command.Institution,
                command.AccountType,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (updated.DuplicateName)
        {
            return output.WithError(FinancialAccountMessages.DuplicateName);
        }

        if (updated.Account is null)
        {
            return output.WithError(FinancialAccountMessages.NotFound);
        }

        var account = updated.Account;
        return output
            .WithData(new UpdateFinancialAccountCommandOutput
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
            .WithMessage(FinancialAccountMessages.UpdatedSuccessfully);
    }
}
