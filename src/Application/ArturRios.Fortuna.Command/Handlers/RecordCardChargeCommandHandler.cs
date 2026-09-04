using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordCardChargeCommandHandler(
    IValidator<RecordCardChargeCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICardChargeStore charges,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordCardChargeCommand, RecordCardChargeCommandOutput>
{
    public async Task<DataOutput<RecordCardChargeCommandOutput?>> HandleAsync(
        RecordCardChargeCommand command)
    {
        var output = DataOutput<RecordCardChargeCommandOutput?>.New;
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
            return output
                .WithError(TransactionMessages.ProfileNotFound);
        }

        var result = await charges.CreateAsync(
            new CardChargeCreation(
                profile.Id,
                command.CreditCardId,
                command.Amount,
                command.OccurredOn,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.CardNotFound || result.Charge is null)
        {
            return output
                .WithError(TransactionMessages.CreditCardNotFound);
        }

        var charge = result.Charge;
        return output
            .WithData(new RecordCardChargeCommandOutput
            {
                Id = charge.Id,
                CreditCardId = charge.CreditCardId,
                Amount = charge.Amount,
                OccurredOn = charge.OccurredOn,
                IsLateArriving = charge.IsLateArriving,
                StatementId = charge.StatementId,
                StatementPeriodStart = charge.StatementPeriodStart,
                StatementPeriodEnd = charge.StatementPeriodEnd,
                StatementClosingDate = charge.StatementClosingDate,
                StatementDueDate = charge.StatementDueDate,
                StatementStatus = charge.StatementStatus,
                StatementPurchaseTotal = charge.StatementPurchaseTotal
            })
            .WithMessage(TransactionMessages.CardChargeCreatedSuccessfully);
    }
}
