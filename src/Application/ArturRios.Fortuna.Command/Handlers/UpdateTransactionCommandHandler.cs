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

public sealed class UpdateTransactionCommandHandler(
    IValidator<UpdateTransactionCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransactionUpdater transactions,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateTransactionCommand, UpdateTransactionCommandOutput>
{
    public async Task<DataOutput<UpdateTransactionCommandOutput?>> HandleAsync(
        UpdateTransactionCommand command)
    {
        var output = DataOutput<UpdateTransactionCommandOutput?>.New;
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
            return output.WithError(TransactionMessages.ProfileNotFound);
        }

        var result = await transactions.UpdateAsync(
            new TransactionUpdate(
                profile.Id,
                command.Id,
                command.CategoryId,
                command.Direction,
                command.Amount,
                command.OccurredOn,
                command.Description,
                command.Counterparty,
                command.Tags ?? [],
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome != TransactionUpdateOutcome.Succeeded ||
            result.Transaction is null)
        {
            return output.WithError(result.Outcome switch
            {
                TransactionUpdateOutcome.NotFound => TransactionMessages.NotFound,
                TransactionUpdateOutcome.CategoryNotFound => TransactionMessages.CategoryNotFound,
                TransactionUpdateOutcome.SettledStatementFrozen =>
                    TransactionMessages.SettledStatementFrozen,
                TransactionUpdateOutcome.TransferFieldsRestricted =>
                    TransactionMessages.TransferFieldsRestricted,
                TransactionUpdateOutcome.InstallmentFieldsRestricted =>
                    TransactionMessages.InstallmentFieldsRestricted,
                _ => throw new InvalidOperationException("Unknown transaction update outcome.")
            });
        }

        var transaction = result.Transaction;
        return output
            .WithData(new UpdateTransactionCommandOutput
            {
                Id = transaction.Id,
                FinancialAccountId = transaction.FinancialAccountId,
                FinancialAccountName = transaction.FinancialAccountName,
                CreditCardId = transaction.CreditCardId,
                CreditCardName = transaction.CreditCardName,
                CategoryId = transaction.CategoryId,
                CategoryName = transaction.CategoryName,
                CounterpartyId = transaction.CounterpartyId,
                CounterpartyName = transaction.CounterpartyName,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                CurrencyCode = transaction.CurrencyCode,
                OriginalAmount = transaction.OriginalAmount,
                OriginalCurrencyCode = transaction.OriginalCurrencyCode,
                AppliedRate = transaction.AppliedRate,
                RateDate = transaction.RateDate,
                OccurredOn = transaction.OccurredOn,
                Description = transaction.Description,
                SourceType = transaction.SourceType,
                IsReconciled = transaction.IsReconciled,
                IsManuallyCorrected = transaction.IsManuallyCorrected,
                IsTransfer = transaction.IsTransfer,
                StatementId = transaction.StatementId,
                IsLateArriving = transaction.IsLateArriving,
                Tags = transaction.Tags.Select(tag => new UpdateTransactionTagOutput
                {
                    Id = tag.Id,
                    Name = tag.Name
                }).ToArray(),
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt
            })
            .WithMessage(TransactionMessages.UpdatedSuccessfully);
    }
}
