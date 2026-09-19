using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateCreditCardCommandHandler(
    ICurrentProfileResolver profileResolver,
    ICreditCardUpdater cards,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateCreditCardCommand, UpdateCreditCardCommandOutput>
{
    public async Task<DataOutput<UpdateCreditCardCommandOutput?>> HandleAsync(
        UpdateCreditCardCommand command)
    {
        var output = DataOutput<UpdateCreditCardCommandOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardMessages.ProfileNotFound);
        }

        var updated = await cards.UpdateAsync(
            new CreditCardUpdate(
                profile.Id,
                command.Id,
                command.Name.Trim(),
                command.Issuer.Trim(),
                command.CreditLimit,
                command.ClosingDay,
                command.DueDay,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (updated.DuplicateName)
        {
            return output.WithError(CreditCardMessages.DuplicateName);
        }

        if (updated.Card is null)
        {
            return output.WithError(CreditCardMessages.NotFound);
        }

        var card = updated.Card;

        return output
            .WithData(new UpdateCreditCardCommandOutput
            {
                Id = card.Id,
                Name = card.Name,
                Issuer = card.Issuer,
                CurrencyCode = card.CurrencyCode,
                CreditLimit = card.CreditLimit,
                ClosingDay = card.ClosingDay,
                DueDay = card.DueDay,
                LastFourDigits = card.LastFourDigits,
                CreatedAt = card.CreatedAt,
                UpdatedAt = card.UpdatedAt
            })
            .WithMessage(CreditCardMessages.UpdatedSuccessfully);
    }
}
