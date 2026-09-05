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

public sealed class ReconcileTransactionCommandHandler(
    IValidator<ReconcileTransactionCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransactionReconciliationStore transactions,
    ReconciliationOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<ReconcileTransactionCommand, ReconcileTransactionCommandOutput>
{
    public async Task<DataOutput<ReconcileTransactionCommandOutput?>> HandleAsync(
        ReconcileTransactionCommand command)
    {
        var output = DataOutput<ReconcileTransactionCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(
                    actor.SubjectId,
                    CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(TransactionMessages.ProfileNotFound);
        }

        var result = await transactions.ReconcileAsync(
            new TransactionReconciliation(
                profile.Id,
                command.Id,
                command.ImportJobId,
                command.ImportedRecordId,
                command.Unreconcile,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome != TransactionReconciliationOutcome.Succeeded ||
            result.Transaction is null)
        {
            if (result.Outcome ==
                TransactionReconciliationOutcome.ImportedRecordAlreadyMatched)
            {
                return output.WithErrors([
                    TransactionMessages.ImportedRecordAlreadyMatched,
                    TransactionMessages.ConflictingTransaction(
                        result.ConflictingTransactionId!.Value)
                ]);
            }

            return output.WithError(result.Outcome switch
            {
                TransactionReconciliationOutcome.TransactionNotFound =>
                    TransactionMessages.NotFound,
                TransactionReconciliationOutcome.ImportedRecordNotFound =>
                    TransactionMessages.ImportedRecordNotFound,
                TransactionReconciliationOutcome.TransactionAlreadyReconciled =>
                    TransactionMessages.AlreadyReconciled,
                TransactionReconciliationOutcome.TransactionNotReconciled =>
                    TransactionMessages.NotReconciled,
                TransactionReconciliationOutcome.SettledStatementFrozen =>
                    TransactionMessages.SettledStatementFrozen,
                _ => throw new InvalidOperationException(
                    "Unknown transaction reconciliation outcome.")
            });
        }

        var transaction = result.Transaction;
        var reconciliation = CreateReconciliation(transaction);
        return output
            .WithData(new ReconcileTransactionCommandOutput
            {
                Id = transaction.Id,
                Amount = transaction.Amount,
                CurrencyCode = transaction.CurrencyCode,
                OccurredOn = transaction.OccurredOn,
                IsReconciled = transaction.IsReconciled,
                Reconciliation = reconciliation,
                UpdatedAt = transaction.UpdatedAt
            })
            .WithMessage(command.Unreconcile
                ? TransactionMessages.UnreconciledSuccessfully
                : TransactionMessages.ReconciledSuccessfully);
    }

    private TransactionReconciliationOutput? CreateReconciliation(
        TransactionReadSnapshot transaction)
    {
        if (!transaction.IsReconciled ||
            !transaction.ImportJobId.HasValue ||
            !transaction.ImportedRecordId.HasValue ||
            !transaction.ImportedAmount.HasValue ||
            !transaction.ImportedOccurredOn.HasValue)
        {
            return null;
        }

        var amountDiffers = Math.Abs(transaction.Amount - transaction.ImportedAmount.Value) >
            options.AmountTolerance;
        var dateDiffers = Math.Abs(
            transaction.OccurredOn.DayNumber - transaction.ImportedOccurredOn.Value.DayNumber) >
            options.DateToleranceDays;
        return new TransactionReconciliationOutput
        {
            ImportJobId = transaction.ImportJobId.Value,
            ImportedRecordId = transaction.ImportedRecordId.Value,
            HasDiscrepancy = amountDiffers || dateDiffers,
            TransactionAmount = transaction.Amount,
            ImportedAmount = transaction.ImportedAmount.Value,
            TransactionOccurredOn = transaction.OccurredOn,
            ImportedOccurredOn = transaction.ImportedOccurredOn.Value
        };
    }
}
