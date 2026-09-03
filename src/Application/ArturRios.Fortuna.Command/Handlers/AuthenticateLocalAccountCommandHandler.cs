using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Hashing;
using ArturRios.Util.Random;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class AuthenticateLocalAccountCommandHandler(
    ILocalAccountStore accounts,
    ILocalAuthTokenIssuer tokenIssuer,
    LocalAccountOptions options)
    : ICommandHandlerAsync<AuthenticateLocalAccountCommand, AuthenticateLocalAccountCommandOutput>
{
    private static readonly Lazy<(byte[] Hash, byte[] Salt)> DummyCredentials = new(CreateDummyCredentials);

    public async Task<DataOutput<AuthenticateLocalAccountCommandOutput?>> HandleAsync(
        AuthenticateLocalAccountCommand command)
    {
        var output = DataOutput<AuthenticateLocalAccountCommandOutput?>.New;
        if (!options.Enabled)
        {
            return output.WithError(LocalAccountMessages.Disabled);
        }

        var dummy = DummyCredentials.Value;
        var account = await accounts.FindForAuthenticationAsync(command.Name ?? string.Empty, CancellationToken.None);
        var hash = account?.SecretHash ?? dummy.Hash;
        var salt = account?.Salt ?? dummy.Salt;
        var secretMatches = Hash.TextMatches(command.Secret ?? string.Empty, hash, salt);

        if (account is null || !secretMatches)
        {
            return output.WithError(LocalAuthenticationMessages.InvalidCredentials);
        }

        var token = tokenIssuer.Issue(account.UserId, account.DisplayName);

        return output
            .WithData(new AuthenticateLocalAccountCommandOutput
            {
                Token = token.Token,
                ExpiresAt = token.ExpiresAt
            })
            .WithMessage(LocalAuthenticationMessages.AuthenticatedSuccessfully);
    }

    private static (byte[] Hash, byte[] Salt) CreateDummyCredentials()
    {
        var secret = CustomRandom.Text(new RandomStringOptions { Length = 32 });
        var hash = Hash.EncodeWithRandomSalt(secret, out var salt);

        return (hash, salt);
    }
}
