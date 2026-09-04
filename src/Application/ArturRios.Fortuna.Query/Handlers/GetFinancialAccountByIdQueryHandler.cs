using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetFinancialAccountByIdQueryHandler(
    IUserProfileReader profiles,
    IFinancialAccountReader accounts,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetFinancialAccountByIdQuery, FinancialAccountOutput>
{
    public async Task<DataOutput<FinancialAccountOutput?>> HandleAsync(
        GetFinancialAccountByIdQuery query)
    {
        var output = DataOutput<FinancialAccountOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(FinancialAccountMessages.ProfileNotFound);
        }

        var account = await accounts.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        if (account is null)
        {
            return output.WithError(FinancialAccountMessages.NotFound);
        }

        return output
            .WithData(Project(account))
            .WithMessage(FinancialAccountMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    internal static FinancialAccountOutput Project(FinancialAccountSnapshot account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Institution = account.Institution,
        AccountType = account.AccountType,
        CurrencyCode = account.CurrencyCode,
        OpeningBalance = account.OpeningBalance,
        IsDeleted = account.IsDeleted,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt
    };
}
