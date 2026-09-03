using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetMyProfileQueryHandler(IUserProfileReader profiles)
    : IQueryHandlerAsync<GetMyProfileQuery, UserProfileOutput>
{
    public async Task<DataOutput<UserProfileOutput?>> HandleAsync(GetMyProfileQuery query)
    {
        var profile = query.IsLocal
            ? await profiles.FindByPublicIdAsync(query.ExternalSubject, CancellationToken.None)
            : await profiles.FindByExternalSubjectAsync(query.ExternalSubject, CancellationToken.None);
        if (profile is null)
        {
            return DataOutput<UserProfileOutput?>.New.WithError(UserProfileMessages.ProfileNotFound);
        }

        return DataOutput<UserProfileOutput?>.New
            .WithData(new UserProfileOutput
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                DisplayCurrency = profile.DisplayCurrency,
                DisplayCurrencyRequiresConfirmation = profile.DisplayCurrencyRequiresConfirmation,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            })
            .WithMessage(UserProfileMessages.ProfileRetrievedSuccessfully);
    }
}
