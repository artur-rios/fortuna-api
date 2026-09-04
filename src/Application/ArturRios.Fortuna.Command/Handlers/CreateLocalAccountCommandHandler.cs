using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Hashing;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateLocalAccountCommandHandler(
    IValidator<CreateLocalAccountCommand> validator,
    ILocalAccountStore accounts,
    ILocalCredentialStoreAvailability credentialStore,
    ILocalRecoveryCodeGenerator recoveryCodeGenerator,
    LocalAccountOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateLocalAccountCommand, CreateLocalAccountCommandOutput>
{
    public async Task<DataOutput<CreateLocalAccountCommandOutput?>> HandleAsync(
        CreateLocalAccountCommand command)
    {
        var output = DataOutput<CreateLocalAccountCommandOutput?>.New;
        if (!options.Enabled)
        {
            return output.WithError(LocalAccountMessages.Disabled);
        }

        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        if (!credentialStore.IsAvailable(command.StorageMode))
        {
            return output.WithError(LocalAccountMessages.CredentialStoreUnavailable);
        }

        if (await accounts.ExistsAsync(CancellationToken.None))
        {
            return output.WithError(LocalAccountMessages.AlreadyExists);
        }

        var secretHash = Hash.EncodeWithRandomSalt(command.Secret, out var salt);
        var recoveryCodes = recoveryCodeGenerator.Generate(options.RecoveryCodeCount);
        var creation = await accounts.CreateAsync(
            new LocalAccountCreation(
                command.DisplayName,
                secretHash,
                salt,
                command.StorageMode,
                recoveryCodes.Select(code => code.Hash).ToArray(),
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        if (creation.AlreadyExists)
        {
            return output.WithError(LocalAccountMessages.AlreadyExists);
        }

        var account = creation.Account!;

        return output
            .WithData(new CreateLocalAccountCommandOutput
            {
                Id = account.Id,
                UserId = account.UserId,
                DisplayName = account.DisplayName,
                StorageMode = account.StorageMode,
                RecoveryCodes = recoveryCodes.Select(code => code.Value).ToArray(),
                RecoveryWarning = LocalAccountMessages.RecoveryWarning,
                CreatedAt = account.CreatedAt
            })
            .WithMessage(LocalAccountMessages.CreatedSuccessfully);
    }

}
