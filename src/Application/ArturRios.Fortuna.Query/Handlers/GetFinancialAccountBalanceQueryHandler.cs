using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetFinancialAccountBalanceQueryHandler(
    IUserProfileReader profiles,
    IFinancialAccountReader accounts,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetFinancialAccountBalanceQuery, FinancialAccountBalanceOutput>
{
    public async Task<DataOutput<FinancialAccountBalanceOutput?>> HandleAsync(
        GetFinancialAccountBalanceQuery query)
    {
        var output = DataOutput<FinancialAccountBalanceOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var asOf = query.AsOf ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var balance = await accounts.CalculateBalanceAsync(
            profile.Id,
            query.Id,
            asOf,
            CancellationToken.None);
        if (balance is null)
        {
            return output.WithError(FinancialAccountMessages.NotFound);
        }

        return output
            .WithData(new FinancialAccountBalanceOutput
            {
                Id = balance.Id,
                CurrencyCode = balance.CurrencyCode,
                Balance = balance.Balance,
                AsOf = balance.AsOf
            })
            .WithMessage(FinancialAccountMessages.BalanceRetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
}
