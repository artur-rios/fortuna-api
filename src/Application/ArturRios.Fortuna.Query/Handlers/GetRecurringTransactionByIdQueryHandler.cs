using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetRecurringTransactionByIdQueryHandler(
    IValidator<GetRecurringTransactionByIdQuery> validator,
    IUserProfileReader profiles,
    IRecurringTransactionReader rules,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetRecurringTransactionByIdQuery, RecurringTransactionOutput>
{
    public async Task<DataOutput<RecurringTransactionOutput?>> HandleAsync(
        GetRecurringTransactionByIdQuery query)
    {
        var output = DataOutput<RecurringTransactionOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(item => item.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        }

        var rule = await rules.FindByIdAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (rule is null)
        {
            return output.WithError(RecurringTransactionMessages.NotFound);
        }

        return output.WithData(new RecurringTransactionOutput
        {
            Id = rule.Id,
            FinancialAccountId = rule.FinancialAccountId,
            CreditCardId = rule.CreditCardId,
            CategoryId = rule.CategoryId,
            Direction = rule.Direction,
            Amount = rule.Amount,
            CurrencyCode = rule.CurrencyCode,
            Frequency = rule.Frequency,
            StartsOn = rule.StartsOn,
            EndsOn = rule.EndsOn,
            LastMaterializedOn = rule.LastMaterializedOn,
            Description = rule.Description,
            CounterpartyId = rule.CounterpartyId,
            CounterpartyName = rule.CounterpartyName,
            NextOccurrences = rule.NextOccurrences,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        }).WithMessage(RecurringTransactionMessages.RetrievedSuccessfully);
    }
}
