using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateInvestmentCommandHandler(
    IValidator<UpdateInvestmentCommand> validator,
    ICurrentProfileResolver profileResolver,
    IInvestmentUpdater investments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateInvestmentCommand, UpdateInvestmentCommandOutput>
{
    public async Task<DataOutput<UpdateInvestmentCommandOutput?>> HandleAsync(
        UpdateInvestmentCommand command)
    {
        var output = DataOutput<UpdateInvestmentCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        var updated = await investments.UpdateAsync(
            new InvestmentUpdate(
                profile.Id,
                command.Id,
                command.Instrument.Trim(),
                command.Institution,
                command.InvestmentType,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (updated.DuplicateInstrument)
        {
            return output.WithError(InvestmentMessages.DuplicateInstrument);
        }

        if (updated.Investment is null)
        {
            return output.WithError(InvestmentMessages.NotFound);
        }

        var investment = updated.Investment;

        return output
            .WithData(new UpdateInvestmentCommandOutput
            {
                Id = investment.Id,
                Instrument = investment.Instrument,
                Institution = investment.Institution,
                InvestmentType = investment.InvestmentType,
                CurrencyCode = investment.CurrencyCode,
                CreatedAt = investment.CreatedAt,
                UpdatedAt = investment.UpdatedAt
            })
            .WithMessage(InvestmentMessages.UpdatedSuccessfully);
    }
}
