using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(CommandMediator commandMediator) : Controller
{
    public const string AnonymousRateLimitPolicy = "AuthAnonymous";

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [HeimdallAuthMessages.AuthenticationRejected] = StatusCodes.Status401Unauthorized,
            [HeimdallAuthMessages.TwoFactorRejected] = StatusCodes.Status401Unauthorized,
            [HeimdallAuthMessages.TwoFactorNotFound] = StatusCodes.Status404NotFound,
            [HeimdallAuthMessages.ServiceUnavailable] = StatusCodes.Status503ServiceUnavailable
        };

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<LoginThroughApiCommandOutput?>>> Login(
        [FromBody] LoginThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            LoginThroughApiCommand, LoginThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("google")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<GoogleSignInThroughApiCommandOutput?>>> Google(
        [FromBody] GoogleSignInThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            GoogleSignInThroughApiCommand, GoogleSignInThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<VerifyTwoFactorThroughApiCommandOutput?>>> VerifyTwoFactor(
        [FromBody] VerifyTwoFactorThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            VerifyTwoFactorThroughApiCommand, VerifyTwoFactorThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("google/sign-out")]
    public async Task<ActionResult<DataOutput<GoogleSignOutThroughApiCommandOutput?>>> GoogleSignOut()
    {
        var command = new GoogleSignOutThroughApiCommand
        {
            BearerToken = BearerToken()
        };
        var result = await commandMediator.ExecuteCommandAsync<
            GoogleSignOutThroughApiCommand, GoogleSignOutThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("password-recovery")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>>>
        RequestPasswordRecovery([FromBody] RequestPasswordRecoveryThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RequestPasswordRecoveryThroughApiCommand,
            RequestPasswordRecoveryThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("password-reset")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<ResetPasswordThroughApiCommandOutput?>>> ResetPassword(
        [FromBody] ResetPasswordThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            ResetPasswordThroughApiCommand, ResetPasswordThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<VerifyEmailThroughApiCommandOutput?>>> VerifyEmail(
        [FromBody] VerifyEmailThroughApiCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            VerifyEmailThroughApiCommand, VerifyEmailThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult<DataOutput<ResendVerificationThroughApiCommandOutput?>>>
        ResendVerification()
    {
        var result = await commandMediator.ExecuteCommandAsync<
            ResendVerificationThroughApiCommand, ResendVerificationThroughApiCommandOutput>(
                new() { BearerToken = BearerToken() });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("2fa")]
    public async Task<ActionResult<DataOutput<GetTwoFactorStatusThroughApiCommandOutput?>>>
        GetTwoFactorStatus()
    {
        var result = await commandMediator.ExecuteCommandAsync<
            GetTwoFactorStatusThroughApiCommand, GetTwoFactorStatusThroughApiCommandOutput>(
                new() { BearerToken = BearerToken() });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("2fa/enable")]
    public async Task<ActionResult<DataOutput<EnableTwoFactorThroughApiCommandOutput?>>> EnableTwoFactor(
        [FromBody] EnableTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();
        var result = await commandMediator.ExecuteCommandAsync<
            EnableTwoFactorThroughApiCommand, EnableTwoFactorThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("2fa/confirm")]
    public async Task<ActionResult<DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>>> ConfirmTwoFactor(
        [FromBody] ConfirmTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();
        var result = await commandMediator.ExecuteCommandAsync<
            ConfirmTwoFactorThroughApiCommand, ConfirmTwoFactorThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("2fa/disable")]
    public async Task<ActionResult<DataOutput<DisableTwoFactorThroughApiCommandOutput?>>> DisableTwoFactor(
        [FromBody] DisableTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();
        var result = await commandMediator.ExecuteCommandAsync<
            DisableTwoFactorThroughApiCommand, DisableTwoFactorThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("2fa/recovery-codes/regenerate")]
    public async Task<ActionResult<DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>>>
        RegenerateRecoveryCodes([FromBody] RegenerateRecoveryCodesThroughApiCommand command)
    {
        command.BearerToken = BearerToken();
        var result = await commandMediator.ExecuteCommandAsync<
            RegenerateRecoveryCodesThroughApiCommand,
            RegenerateRecoveryCodesThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    private string BearerToken() =>
        Request.Headers.Authorization.ToString()["Bearer ".Length..];
}
