using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetMyProcessingConsentsQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IProcessingConsentReader consents,
    ProcessingConsentOptions options)
    : IQueryHandlerAsync<GetMyProcessingConsentsQuery, ProcessingConsentQueryOutput>
{
    public async Task<DataOutput<ProcessingConsentQueryOutput?>> HandleAsync(
        GetMyProcessingConsentsQuery query)
    {
        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return DataOutput<ProcessingConsentQueryOutput?>.New.WithError(
                ProcessingConsentMessages.ProfileNotFound);
        }
        var granted = await consents.ListAsync(profile.Id, CancellationToken.None);
        var byPurpose = granted.ToDictionary(item => item.Purpose);
        var states = Enum.GetValues<ProcessingConsentPurpose>()
            .Select(purpose =>
            {
                byPurpose.TryGetValue(purpose, out var consent);
                var current = options.CurrentVersion(purpose);
                return new ProcessingConsentStateOutput
                {
                    Purpose = ProcessingConsentOptions.Name(purpose),
                    CurrentVersion = current,
                    GrantedVersion = consent?.Version,
                    GrantedAt = consent?.GrantedAt,
                    IsCurrent = consent is not null &&
                        string.Equals(consent.Version, current, StringComparison.Ordinal)
                };
            })
            .ToArray();
        return DataOutput<ProcessingConsentQueryOutput?>.New
            .WithData(new ProcessingConsentQueryOutput { Consents = states })
            .WithMessage(ProcessingConsentMessages.RetrievedSuccessfully);
    }
}
