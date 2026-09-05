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

public sealed class RecordTransactionCommandHandler(
    IValidator<RecordTransactionCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransactionStore transactions,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordTransactionCommand, RecordTransactionCommandOutput>
{
    public async Task<DataOutput<RecordTransactionCommandOutput?>> HandleAsync(
        RecordTransactionCommand command)
    {
        var output = DataOutput<RecordTransactionCommandOutput?>.New;
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

        var result = await transactions.RecordAsync(
            new TransactionRecord(
                profile.Id,
                command.FinancialAccountId,
                command.CreditCardId,
                command.CategoryId,
                command.Direction,
                command.Amount,
                command.CurrencyCode,
                command.OccurredOn,
                command.Description,
                command.Counterparty,
                command.Tags ?? [],
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome != TransactionRecordOutcome.Succeeded ||
            result.Transaction is null)
        {
            return output.WithError(result.Outcome switch
            {
                TransactionRecordOutcome.FinancialAccountNotFound =>
                    TransactionMessages.FinancialAccountNotFound,
                TransactionRecordOutcome.CreditCardNotFound =>
                    TransactionMessages.CreditCardNotFound,
                TransactionRecordOutcome.CategoryNotFound =>
                    TransactionMessages.CategoryNotFound,
                TransactionRecordOutcome.CurrencyNotSupported =>
                    TransactionMessages.CurrencyNotSupported,
                TransactionRecordOutcome.ExchangeRateUnavailable =>
                    TransactionMessages.ExchangeRateUnavailable,
                TransactionRecordOutcome.ConvertedAmountTooSmall =>
                    TransactionMessages.ConvertedAmountTooSmall,
                _ => throw new InvalidOperationException("Unknown transaction record outcome.")
            });
        }

        var transaction = result.Transaction;
        return output
            .WithData(new RecordTransactionCommandOutput
            {
                Id = transaction.Id,
                FinancialAccountId = transaction.FinancialAccountId,
                CreditCardId = transaction.CreditCardId,
                CategoryId = transaction.CategoryId,
                CategoryName = transaction.CategoryName,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                CurrencyCode = transaction.CurrencyCode,
                OriginalAmount = transaction.OriginalAmount,
                OriginalCurrencyCode = transaction.OriginalCurrencyCode,
                AppliedRate = transaction.AppliedRate,
                RateDate = transaction.RateDate,
                OccurredOn = transaction.OccurredOn,
                Description = transaction.Description,
                CounterpartyId = transaction.CounterpartyId,
                CounterpartyName = transaction.CounterpartyName,
                Tags = transaction.Tags.Select(tag => new TransactionTagOutput
                {
                    Id = tag.Id,
                    Name = tag.Name
                }).ToArray(),
                StatementId = transaction.StatementId,
                StatementPeriodStart = transaction.StatementPeriodStart,
                StatementPeriodEnd = transaction.StatementPeriodEnd,
                StatementClosingDate = transaction.StatementClosingDate,
                StatementDueDate = transaction.StatementDueDate,
                StatementStatus = transaction.StatementStatus,
                StatementPurchaseTotal = transaction.StatementPurchaseTotal,
                IsLateArriving = transaction.IsLateArriving,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt
            })
            .WithMessage(TransactionMessages.RecordedSuccessfully);
    }
}
