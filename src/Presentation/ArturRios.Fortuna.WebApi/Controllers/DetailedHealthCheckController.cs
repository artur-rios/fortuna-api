using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("healthcheck/detailed")]
public sealed class DetailedHealthCheckController(
    OperationalHealthEvaluator health) : ControllerBase
{
    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.SystemAdmin)]
    [ProducesResponseType(typeof(OperationalHealthOutput), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationalHealthOutput),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await health.EvaluateAsync(cancellationToken);
        var response = OperationalHealthOutput.From(report);
        return report.Status == OperationalHealthStatus.Unhealthy
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, response)
            : Ok(response);
    }
}
