using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Output;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/local-accounts")]
public sealed class LocalAccountsController(
    LocalAccountOptions options) : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [LocalAccountMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [LocalAccountMessages.Disabled] = StatusCodes.Status404NotFound,
            [LocalAccountMessages.AlreadyExists] = StatusCodes.Status409Conflict,
            [LocalAccountMessages.CredentialStoreUnavailable] = StatusCodes.Status400BadRequest,
            [LocalAuthenticationMessages.InvalidCredentials] = StatusCodes.Status401Unauthorized,
            [LocalAuthenticationMessages.PasswordResetUnavailable] = StatusCodes.Status404NotFound,
            [LocalAccountRecoveryMessages.InvalidRecoveryCode] = StatusCodes.Status401Unauthorized,
            [LocalAccountRecoveryMessages.RecoveryCodesExhausted] = StatusCodes.Status401Unauthorized,
            [LocalRecoveryCodeRegenerationMessages.InvalidSecret] = StatusCodes.Status401Unauthorized,
            [LocalRecoveryCodeRegenerationMessages.LocalAccountOnly] = StatusCodes.Status404NotFound
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<DataOutput<CreateLocalAccountCommandOutput?>>> Create(
        [FromBody] CreateLocalAccountCommand command)
    {
        return await SendAsync<CreateLocalAccountCommand, CreateLocalAccountCommandOutput>(command);
    }

    [HttpPost("authenticate")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthController.AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<AuthenticateLocalAccountCommandOutput?>>> Authenticate(
        [FromBody] AuthenticateLocalAccountCommand command)
    {
        return await SendAsync<AuthenticateLocalAccountCommand, AuthenticateLocalAccountCommandOutput>(command);
    }

    [HttpPost("recover")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthController.AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<RecoverLocalAccountCommandOutput?>>> Recover(
        [FromBody] RecoverLocalAccountCommand command)
    {
        return await SendAsync<RecoverLocalAccountCommand, RecoverLocalAccountCommandOutput>(command);
    }

    [HttpPost("recovery-codes/regenerate")]
    public async Task<ActionResult<DataOutput<RegenerateLocalAccountRecoveryCodesCommandOutput?>>> Regenerate(
        [FromBody] RegenerateLocalAccountRecoveryCodesCommand command)
    {
        return await SendAsync<
            RegenerateLocalAccountRecoveryCodesCommand,
            RegenerateLocalAccountRecoveryCodesCommandOutput>(command);
    }

    [HttpPost("password-reset")]
    [AllowAnonymous]
    public ActionResult<DataOutput<object?>> PasswordReset()
    {
        var error = options.Enabled
            ? LocalAuthenticationMessages.PasswordResetUnavailable
            : LocalAccountMessages.Disabled;

        return Respond(DataOutput<object?>.New.WithError(error));
    }
}
