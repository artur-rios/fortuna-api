using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetRecurringTransactionByIdQueryHandler(
    IValidator<GetRecurringTransactionByIdQuery> validator,
    ICurrentProfileResolver profileResolver,
    IRecurringTransactionReader rules)
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

        var profile = await profileResolver.ResolveAsync();
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

        return output
            .WithData(RecurringTransactionProjection.Project(rule))
            .WithMessage(RecurringTransactionMessages.RetrievedSuccessfully);
    }
}
