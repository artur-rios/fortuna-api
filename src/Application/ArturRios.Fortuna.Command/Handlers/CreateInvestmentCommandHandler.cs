using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateInvestmentCommandHandler(
    IValidator<CreateInvestmentCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICurrencyReader currencies,
    IInvestmentStore investments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateInvestmentCommand, CreateInvestmentCommandOutput>
{
    public async Task<DataOutput<CreateInvestmentCommandOutput?>> HandleAsync(
        CreateInvestmentCommand command)
    {
        var output = DataOutput<CreateInvestmentCommandOutput?>.New;
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
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        var currencyCode = command.CurrencyCode.Trim().ToUpperInvariant();
        if (await currencies.FindByCodeAsync(currencyCode, CancellationToken.None) is null)
        {
            return output
                .WithError(InvestmentMessages.CurrencyNotSupported)
                .WithMessage(InvestmentMessages.UnknownCurrency(currencyCode));
        }

        var created = await investments.CreateAsync(
            new InvestmentCreation(
                profile.Id,
                command.Instrument.Trim(),
                command.Institution,
                command.InvestmentType,
                currencyCode,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (created.DuplicateInstrument)
        {
            return output.WithError(InvestmentMessages.DuplicateInstrument);
        }

        var investment = created.Investment!;
        return output
            .WithData(new CreateInvestmentCommandOutput
            {
                Id = investment.Id,
                Instrument = investment.Instrument,
                Institution = investment.Institution,
                InvestmentType = investment.InvestmentType,
                CurrencyCode = investment.CurrencyCode,
                CreatedAt = investment.CreatedAt,
                UpdatedAt = investment.UpdatedAt
            })
            .WithMessage(InvestmentMessages.CreatedSuccessfully);
    }
}
