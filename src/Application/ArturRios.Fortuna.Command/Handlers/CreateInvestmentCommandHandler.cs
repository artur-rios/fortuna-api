using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateInvestmentCommandHandler(
    ICurrentProfileResolver profileResolver,
    ICurrencyReader currencies,
    IInvestmentStore investments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateInvestmentCommand, CreateInvestmentCommandOutput>
{
    public async Task<DataOutput<CreateInvestmentCommandOutput?>> HandleAsync(
        CreateInvestmentCommand command)
    {
        var output = DataOutput<CreateInvestmentCommandOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
