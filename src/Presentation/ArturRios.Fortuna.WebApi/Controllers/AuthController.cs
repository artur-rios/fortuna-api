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
            BearerToken = Request.Headers.Authorization.ToString()["Bearer ".Length..]
        };
        var result = await commandMediator.ExecuteCommandAsync<
            GoogleSignOutThroughApiCommand, GoogleSignOutThroughApiCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
