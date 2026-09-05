using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordInvestmentValuationCommandHandler(
    IValidator<RecordInvestmentValuationCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInvestmentValuationStore valuations,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordInvestmentValuationCommand,
        RecordInvestmentValuationCommandOutput>
{
    public async Task<DataOutput<RecordInvestmentValuationCommandOutput?>> HandleAsync(
        RecordInvestmentValuationCommand command)
    {
        var output = DataOutput<RecordInvestmentValuationCommandOutput?>.New;
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

        var result = await valuations.RecordAsync(
            new InvestmentValuationRecord(
                profile.Id,
                command.Id,
                command.Value,
                command.ValuedOn,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome != InvestmentValuationRecordOutcome.Succeeded ||
            result.Valuation is null)
        {
            return output.WithError(result.Outcome switch
            {
                InvestmentValuationRecordOutcome.InvestmentNotFound =>
                    InvestmentMessages.NotFound,
                _ => throw new InvalidOperationException("Unknown investment valuation outcome.")
            });
        }

        var valuation = result.Valuation;
        return output
            .WithData(new RecordInvestmentValuationCommandOutput
            {
                Id = valuation.Id,
                InvestmentId = valuation.InvestmentId,
                Value = valuation.Value,
                CurrencyCode = valuation.CurrencyCode,
                ValuedOn = valuation.ValuedOn,
                ReplacedExisting = valuation.ReplacedExisting,
                Position = valuation.Position,
                IsIndependentlyValued = valuation.IsIndependentlyValued,
                LatestValuationValue = valuation.LatestValuationValue,
                LatestValuationDate = valuation.LatestValuationDate,
                CreatedAt = valuation.CreatedAt,
                UpdatedAt = valuation.UpdatedAt
            })
            .WithMessage(valuation.ReplacedExisting
                ? InvestmentMessages.ValuationReplacedSuccessfully
                : InvestmentMessages.ValuationRecordedSuccessfully);
    }
}
