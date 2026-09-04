using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCreditCardStatementByIdQueryHandler(
    IUserProfileReader profiles,
    ICreditCardStatementReader statements,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetCreditCardStatementByIdQuery, CreditCardStatementOutput>
{
    public async Task<DataOutput<CreditCardStatementOutput?>> HandleAsync(
        GetCreditCardStatementByIdQuery query)
    {
        var output = DataOutput<CreditCardStatementOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
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

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
