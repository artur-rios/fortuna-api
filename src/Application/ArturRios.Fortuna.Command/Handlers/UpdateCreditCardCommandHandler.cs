using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateCreditCardCommandHandler(
    IValidator<UpdateCreditCardCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICreditCardUpdater cards,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateCreditCardCommand, UpdateCreditCardCommandOutput>
{
    public async Task<DataOutput<UpdateCreditCardCommandOutput?>> HandleAsync(
        UpdateCreditCardCommand command)
    {
        var output = DataOutput<UpdateCreditCardCommandOutput?>.New;
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
