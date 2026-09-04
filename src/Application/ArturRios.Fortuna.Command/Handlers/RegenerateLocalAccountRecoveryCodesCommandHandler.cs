using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Hashing;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RegenerateLocalAccountRecoveryCodesCommandHandler(
    ILocalAccountStore accounts,
    ILocalRecoveryCodeGenerator recoveryCodeGenerator,
    IRequestActorAccessor actorAccessor,
    LocalAccountOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RegenerateLocalAccountRecoveryCodesCommand,
        RegenerateLocalAccountRecoveryCodesCommandOutput>
{
    public async Task<DataOutput<RegenerateLocalAccountRecoveryCodesCommandOutput?>> HandleAsync(
        RegenerateLocalAccountRecoveryCodesCommand command)
    {
        var output = DataOutput<RegenerateLocalAccountRecoveryCodesCommandOutput?>.New;
        if (!options.Enabled)
        {
            return output.WithError(LocalAccountMessages.Disabled);
        }

        var actor = actorAccessor.Actor;
        if (actor?.IsLocal != true)
        {
            return output.WithError(LocalRecoveryCodeRegenerationMessages.LocalAccountOnly);
        }

        var credentials = await accounts.FindForAuthenticationByUserIdAsync(
            actor.SubjectId,
            CancellationToken.None);
        if (credentials is null ||
            !Hash.TextMatches(command.Secret ?? string.Empty, credentials.SecretHash, credentials.Salt))
        {
            return output.WithError(LocalRecoveryCodeRegenerationMessages.InvalidSecret);
        }

        var recoveryCodes = recoveryCodeGenerator.Generate(options.RecoveryCodeCount);
        var regenerated = await accounts.RegenerateRecoveryCodesAsync(
            new LocalAccountRecoveryCodeRegeneration(
                actor.SubjectId,
                credentials.SecretHash,
                credentials.Salt,
                recoveryCodes.Select(code => code.Hash).ToArray(),
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (!regenerated)
        {
            return output.WithError(LocalRecoveryCodeRegenerationMessages.InvalidSecret);
        }

        return output
            .WithData(new RegenerateLocalAccountRecoveryCodesCommandOutput
            {
                RecoveryCodes = recoveryCodes.Select(code => code.Value).ToArray(),
                RecoveryWarning = LocalAccountMessages.RecoveryWarning
            })
            .WithMessage(LocalRecoveryCodeRegenerationMessages.RegeneratedSuccessfully);
    }
}
