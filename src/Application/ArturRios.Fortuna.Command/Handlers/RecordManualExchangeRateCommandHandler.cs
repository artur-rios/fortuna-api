using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordManualExchangeRateCommandHandler(
    IValidator<RecordManualExchangeRateCommand> validator,
    ICurrencyReader currencies,
    IExchangeRateStore rates)
    : ICommandHandlerAsync<RecordManualExchangeRateCommand, RecordManualExchangeRateCommandOutput>
{
    public async Task<DataOutput<RecordManualExchangeRateCommandOutput?>> HandleAsync(
        RecordManualExchangeRateCommand command)
    {
        var output = DataOutput<RecordManualExchangeRateCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var baseCode = command.BaseCurrencyCode.Trim().ToUpperInvariant();
        var quoteCode = command.QuoteCurrencyCode.Trim().ToUpperInvariant();
        if (await currencies.FindByCodeAsync(baseCode, CancellationToken.None) is null)
        {
            return output
                .WithError(ManualExchangeRateMessages.CurrencyNotSupported)
                .WithMessage(ManualExchangeRateMessages.UnknownCurrency(baseCode));
        }

        if (await currencies.FindByCodeAsync(quoteCode, CancellationToken.None) is null)
        {
            return output
                .WithError(ManualExchangeRateMessages.CurrencyNotSupported)
                .WithMessage(ManualExchangeRateMessages.UnknownCurrency(quoteCode));
        }

        var stored = await rates.UpsertManualAsync(
            new ManualRateCandidate(baseCode, quoteCode, command.Rate, command.RateDate),
            CancellationToken.None);

        return output
            .WithData(new RecordManualExchangeRateCommandOutput
            {
                BaseCurrencyCode = baseCode,
                QuoteCurrencyCode = quoteCode,
                Rate = stored.Rate,
                RateDate = command.RateDate,
                Source = ExchangeRateSource.Manual,
                TakesPrecedence = true,
                ReplacedExisting = stored.ReplacedExisting
            })
            .WithMessage(stored.ReplacedExisting
                ? ManualExchangeRateMessages.ReplacedSuccessfully
                : ManualExchangeRateMessages.RecordedSuccessfully);
    }
}
