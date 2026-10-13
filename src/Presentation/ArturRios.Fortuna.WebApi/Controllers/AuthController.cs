using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : FortunaController
{
    public const string AnonymousRateLimitPolicy = "AuthAnonymous";

    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [HeimdallAuthMessages.AuthenticationRejected] = StatusCodes.Status401Unauthorized,
            [HeimdallAuthMessages.TwoFactorRejected] = StatusCodes.Status401Unauthorized,
            [HeimdallAuthMessages.TwoFactorNotFound] = StatusCodes.Status404NotFound,
            [HeimdallAuthMessages.ServiceUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [HeimdallAuthMessages.TooManyAttempts] = StatusCodes.Status429TooManyRequests,
            [HeimdallAuthMessages.RegistrationRequested] = StatusCodes.Status202Accepted,
            [HeimdallAuthMessages.RegistrationUnavailable] = StatusCodes.Status403Forbidden,
            [HeimdallAuthMessages.ConfirmationPasswordRequired] = StatusCodes.Status422UnprocessableEntity,
            [HeimdallAuthMessages.ConfirmationPasswordIncorrect] = StatusCodes.Status401Unauthorized
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<LoginThroughApiCommandOutput?>>> Login(
        [FromBody] LoginThroughApiCommand command)
    {
        return await SendAsync<
            LoginThroughApiCommand, LoginThroughApiCommandOutput>(command);
    }

    [HttpPost("google")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<GoogleSignInThroughApiCommandOutput?>>> Google(
        [FromBody] GoogleSignInThroughApiCommand command)
    {
        return await SendAsync<
            GoogleSignInThroughApiCommand, GoogleSignInThroughApiCommandOutput>(command);
    }

    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<VerifyTwoFactorThroughApiCommandOutput?>>> VerifyTwoFactor(
        [FromBody] VerifyTwoFactorThroughApiCommand command)
    {
        return await SendAsync<
            VerifyTwoFactorThroughApiCommand, VerifyTwoFactorThroughApiCommandOutput>(command);
    }

    [HttpPost("2fa/challenge/resend")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<ResendTwoFactorChallengeCodeThroughApiCommandOutput?>>>
        ResendTwoFactorChallengeCode(
            [FromBody] ResendTwoFactorChallengeCodeThroughApiCommand command)
    {
        return await SendAsync<
            ResendTwoFactorChallengeCodeThroughApiCommand,
            ResendTwoFactorChallengeCodeThroughApiCommandOutput>(command);
    }

    [HttpPost("google/sign-out")]
    public async Task<ActionResult<DataOutput<GoogleSignOutThroughApiCommandOutput?>>> GoogleSignOut()
    {
        var command = new GoogleSignOutThroughApiCommand
        {
            BearerToken = BearerToken()
        };

        return await SendAsync<
            GoogleSignOutThroughApiCommand, GoogleSignOutThroughApiCommandOutput>(command);
    }

    [HttpPost("password-recovery")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>>>
        RequestPasswordRecovery([FromBody] RequestPasswordRecoveryThroughApiCommand command)
    {
        return await SendAsync<
            RequestPasswordRecoveryThroughApiCommand,
            RequestPasswordRecoveryThroughApiCommandOutput>(command);
    }

    /// <summary>
    ///     Registers a connected identity in Fortuna's Heimdall scope (UC-81). Answers 202 alike for a
    ///     free and a taken address; Heimdall holds only a pending registration, and the account exists,
    ///     and can sign in, only after its holder follows the emailed link and confirms with the
    ///     password chosen at sign-up. The Fortuna profile is created on the first authenticated request.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    [ProducesResponseType(typeof(DataOutput<RegisterThroughApiCommandOutput?>),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(DataOutput<RegisterThroughApiCommandOutput?>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(DataOutput<RegisterThroughApiCommandOutput?>),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(DataOutput<RegisterThroughApiCommandOutput?>),
        StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(DataOutput<RegisterThroughApiCommandOutput?>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DataOutput<RegisterThroughApiCommandOutput?>>> Register(
        [FromBody] RegisterThroughApiCommand command)
    {
        return await SendAsync<RegisterThroughApiCommand, RegisterThroughApiCommandOutput>(command);
    }

    [HttpPost("password-reset")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    public async Task<ActionResult<DataOutput<ResetPasswordThroughApiCommandOutput?>>> ResetPassword(
        [FromBody] ResetPasswordThroughApiCommand command)
    {
        return await SendAsync<
            ResetPasswordThroughApiCommand, ResetPasswordThroughApiCommandOutput>(command);
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(AnonymousRateLimitPolicy)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(DataOutput<VerifyEmailThroughApiCommandOutput?>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DataOutput<VerifyEmailThroughApiCommandOutput?>>> VerifyEmail(
        [FromBody] VerifyEmailThroughApiCommand command)
    {
        return await SendAsync<
            VerifyEmailThroughApiCommand, VerifyEmailThroughApiCommandOutput>(command);
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult<DataOutput<ResendVerificationThroughApiCommandOutput?>>>
        ResendVerification()
    {
        return await SendAsync<
            ResendVerificationThroughApiCommand, ResendVerificationThroughApiCommandOutput>(
                new() { BearerToken = BearerToken() });
    }

    [HttpGet("2fa")]
    public async Task<ActionResult<DataOutput<GetTwoFactorStatusThroughApiCommandOutput?>>>
        GetTwoFactorStatus()
    {
        return await SendAsync<
            GetTwoFactorStatusThroughApiCommand, GetTwoFactorStatusThroughApiCommandOutput>(
                new() { BearerToken = BearerToken() });
    }

    [HttpPost("2fa/enable")]
    public async Task<ActionResult<DataOutput<EnableTwoFactorThroughApiCommandOutput?>>> EnableTwoFactor(
        [FromBody] EnableTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();

        return await SendAsync<
            EnableTwoFactorThroughApiCommand, EnableTwoFactorThroughApiCommandOutput>(command);
    }

    [HttpPost("2fa/confirm")]
    public async Task<ActionResult<DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>>> ConfirmTwoFactor(
        [FromBody] ConfirmTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();

        return await SendAsync<
            ConfirmTwoFactorThroughApiCommand, ConfirmTwoFactorThroughApiCommandOutput>(command);
    }

    [HttpPost("2fa/disable")]
    public async Task<ActionResult<DataOutput<DisableTwoFactorThroughApiCommandOutput?>>> DisableTwoFactor(
        [FromBody] DisableTwoFactorThroughApiCommand command)
    {
        command.BearerToken = BearerToken();

        return await SendAsync<
            DisableTwoFactorThroughApiCommand, DisableTwoFactorThroughApiCommandOutput>(command);
    }

    [HttpPost("2fa/recovery-codes/regenerate")]
    public async Task<ActionResult<DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>>>
        RegenerateRecoveryCodes([FromBody] RegenerateRecoveryCodesThroughApiCommand command)
    {
        command.BearerToken = BearerToken();

        return await SendAsync<
            RegenerateRecoveryCodesThroughApiCommand,
            RegenerateRecoveryCodesThroughApiCommandOutput>(command);
    }

    private string BearerToken() =>
        Request.Headers.Authorization.ToString()["Bearer ".Length..];
}
