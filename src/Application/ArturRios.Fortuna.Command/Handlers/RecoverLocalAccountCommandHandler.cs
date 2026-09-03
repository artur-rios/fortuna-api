using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Hashing;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecoverLocalAccountCommandHandler(
    IValidator<RecoverLocalAccountCommand> validator,
    ILocalAccountStore accounts,
    ILocalAuthTokenIssuer tokenIssuer,
    LocalAccountOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecoverLocalAccountCommand, RecoverLocalAccountCommandOutput>
{
    public async Task<DataOutput<RecoverLocalAccountCommandOutput?>> HandleAsync(
        RecoverLocalAccountCommand command)
    {
        var output = DataOutput<RecoverLocalAccountCommandOutput?>.New;
        if (!options.Enabled)
        {
            return output.WithError(LocalAccountMessages.Disabled);
        }

        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var recoveryCodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(command.RecoveryCode ?? string.Empty));
        var newSecretHash = Hash.EncodeWithRandomSalt(command.NewSecret, out var newSalt);
        var recovery = await accounts.RecoverAsync(
            new LocalAccountRecovery(
                command.Name ?? string.Empty,
                recoveryCodeHash,
                newSecretHash,
                newSalt,
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        if (recovery.Status == LocalAccountRecoveryStatus.Exhausted)
        {
            return output.WithError(LocalAccountRecoveryMessages.RecoveryCodesExhausted);
        }

        if (recovery.Status != LocalAccountRecoveryStatus.Recovered || recovery.Account is null)
        {
            return output.WithError(LocalAccountRecoveryMessages.InvalidRecoveryCode);
        }

        var token = tokenIssuer.Issue(recovery.Account.UserId, recovery.Account.DisplayName);

        return output
            .WithData(new RecoverLocalAccountCommandOutput
            {
                Token = token.Token,
                ExpiresAt = token.ExpiresAt,
                RemainingRecoveryCodes = recovery.Account.RemainingRecoveryCodes
            })
            .WithMessage(LocalAccountRecoveryMessages.RecoveredSuccessfully);
    }
}
