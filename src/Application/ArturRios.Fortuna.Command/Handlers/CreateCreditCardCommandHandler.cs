using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateCreditCardCommandHandler(
    IValidator<CreateCreditCardCommand> validator,
    ICurrentProfileResolver profileResolver,
    ICurrencyReader currencies,
    ICreditCardStore cards,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateCreditCardCommand, CreateCreditCardCommandOutput>
{
    public async Task<DataOutput<CreateCreditCardCommandOutput?>> HandleAsync(
        CreateCreditCardCommand command)
    {
        var output = DataOutput<CreateCreditCardCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardMessages.ProfileNotFound);
        }

        var currencyCode = command.CurrencyCode.Trim().ToUpperInvariant();
        if (await currencies.FindByCodeAsync(currencyCode, CancellationToken.None) is null)
        {
            return output
                .WithError(CreditCardMessages.CurrencyNotSupported)
                .WithMessage(CreditCardMessages.UnknownCurrency(currencyCode));
        }

        var created = await cards.CreateAsync(
            new CreditCardCreation(
                profile.Id,
                command.Name.Trim(),
                command.Issuer.Trim(),
                currencyCode,
                command.CreditLimit,
                command.ClosingDay,
                command.DueDay,
                command.LastFourDigits,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (created.DuplicateName)
        {
            return output.WithError(CreditCardMessages.DuplicateName);
        }

        var card = created.Card!;

        return output
            .WithData(new CreateCreditCardCommandOutput
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
            .WithMessage(CreditCardMessages.CreatedSuccessfully);
    }
}
