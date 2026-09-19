using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateFinancialAccountCommandHandler(
    ICurrentProfileResolver profileResolver,
    IFinancialAccountUpdater accounts,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateFinancialAccountCommand, UpdateFinancialAccountCommandOutput>
{
    public async Task<DataOutput<UpdateFinancialAccountCommandOutput?>> HandleAsync(
        UpdateFinancialAccountCommand command)
    {
        var output = DataOutput<UpdateFinancialAccountCommandOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
