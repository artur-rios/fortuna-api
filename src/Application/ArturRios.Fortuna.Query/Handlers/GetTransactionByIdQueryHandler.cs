using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetTransactionByIdQueryHandler(
    ICurrentProfileResolver profileResolver,
    ITransactionReader transactions)
    : IQueryHandlerAsync<GetTransactionByIdQuery, TransactionOutput>
{
    public async Task<DataOutput<TransactionOutput?>> HandleAsync(GetTransactionByIdQuery query)
    {
        var output = DataOutput<TransactionOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
}
