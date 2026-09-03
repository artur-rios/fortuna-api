using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Hashing;
using ArturRios.Util.Random;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CreateLocalAccountCommandHandler(
    IValidator<CreateLocalAccountCommand> validator,
    ILocalAccountStore accounts,
    ILocalCredentialStoreAvailability credentialStore,
    LocalAccountOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateLocalAccountCommand, CreateLocalAccountCommandOutput>
{
    private const int RecoveryCodeSegmentLength = 4;

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
        var recoveryCodes = GenerateRecoveryCodes(options.RecoveryCodeCount);
        var creation = await accounts.CreateAsync(
            new LocalAccountCreation(
                command.DisplayName,
                secretHash,
                salt,
                command.StorageMode,
                recoveryCodes.Select(HashRecoveryCode).ToArray(),
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
                RecoveryCodes = recoveryCodes,
                RecoveryWarning = LocalAccountMessages.RecoveryWarning,
                CreatedAt = account.CreatedAt
            })
            .WithMessage(LocalAccountMessages.CreatedSuccessfully);
    }

    private static IReadOnlyCollection<string> GenerateRecoveryCodes(int count)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (codes.Count < count)
        {
            var raw = CustomRandom.Text(new RandomStringOptions
            {
                Length = RecoveryCodeSegmentLength * 2,
                IncludeDigits = true,
                IncludeUppercase = true,
                IncludeLowercase = false,
                IncludeSpecialCharacters = false
            });
            codes.Add($"{raw[..RecoveryCodeSegmentLength]}-{raw[RecoveryCodeSegmentLength..]}");
        }

        return [.. codes];
    }

    // Recovery codes are random high-entropy values and the schema intentionally has no per-code
    // salt. Match Heimdall's recovery-code pattern: persist only a one-way SHA-256 digest.
    private static byte[] HashRecoveryCode(string recoveryCode) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(recoveryCode));
}
