using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/local-accounts")]
public sealed class LocalAccountsController(
    CommandMediator commandMediator,
    LocalAccountOptions options) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
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
        };

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<DataOutput<CreateLocalAccountCommandOutput?>>> Create(
        [FromBody] CreateLocalAccountCommand command)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<CreateLocalAccountCommand, CreateLocalAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("authenticate")]
    [AllowAnonymous]
    public async Task<ActionResult<DataOutput<AuthenticateLocalAccountCommandOutput?>>> Authenticate(
        [FromBody] AuthenticateLocalAccountCommand command)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<AuthenticateLocalAccountCommand, AuthenticateLocalAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("recover")]
    [AllowAnonymous]
    public async Task<ActionResult<DataOutput<RecoverLocalAccountCommandOutput?>>> Recover(
        [FromBody] RecoverLocalAccountCommand command)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<RecoverLocalAccountCommand, RecoverLocalAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("recovery-codes/regenerate")]
    public async Task<ActionResult<DataOutput<RegenerateLocalAccountRecoveryCodesCommandOutput?>>> Regenerate(
        [FromBody] RegenerateLocalAccountRecoveryCodesCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RegenerateLocalAccountRecoveryCodesCommand,
            RegenerateLocalAccountRecoveryCodesCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("password-reset")]
    [AllowAnonymous]
    public ActionResult<DataOutput<object?>> PasswordReset()
    {
        var error = options.Enabled
            ? LocalAuthenticationMessages.PasswordResetUnavailable
            : LocalAccountMessages.Disabled;

        return ResponseResolver.Resolve(
            DataOutput<object?>.New.WithError(error),
            statusMap: StatusMap);
    }
}
