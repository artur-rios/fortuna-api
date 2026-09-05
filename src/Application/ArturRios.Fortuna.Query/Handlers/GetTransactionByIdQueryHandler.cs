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

public sealed class GetTransactionByIdQueryHandler(
    IValidator<GetTransactionByIdQuery> validator,
    IUserProfileReader profiles,
    ITransactionReader transactions,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetTransactionByIdQuery, TransactionOutput>
{
    public async Task<DataOutput<TransactionOutput?>> HandleAsync(GetTransactionByIdQuery query)
    {
        var output = DataOutput<TransactionOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(TransactionMessages.ProfileNotFound);
        }

        var transaction = await transactions.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        if (transaction is null)
        {
            return output.WithError(TransactionMessages.NotFound);
        }

        return output
            .WithData(TransactionProjection.Project(transaction))
            .WithMessage(TransactionMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
