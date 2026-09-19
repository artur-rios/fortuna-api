using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCreditCardStatementByIdQueryHandler(
    IValidator<GetCreditCardStatementByIdQuery> validator,
    ICurrentProfileResolver profileResolver,
    ICreditCardStatementReader statements)
    : IQueryHandlerAsync<GetCreditCardStatementByIdQuery, CreditCardStatementOutput>
{
    public async Task<DataOutput<CreditCardStatementOutput?>> HandleAsync(
        GetCreditCardStatementByIdQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<CreditCardStatementOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var output = DataOutput<CreditCardStatementOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardStatementMessages.ProfileNotFound);
        }

        var statement = await statements.FindByIdAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (statement is null)
        {
            return output.WithError(CreditCardStatementMessages.NotFound);
        }

        return output
            .WithData(CreditCardStatementProjection.From(statement))
            .WithMessage(CreditCardStatementMessages.RetrievedSuccessfully);
    }
}
