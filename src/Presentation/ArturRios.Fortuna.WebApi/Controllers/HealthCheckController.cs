using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Output;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("healthcheck")]
public sealed class HealthCheckController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(LivenessOutput), StatusCodes.Status200OK)]
    public ActionResult<LivenessOutput> Get() => Ok(new LivenessOutput
    {
        ContractVersion = ApiContractMetadata.Version,
        Service = ApiContractMetadata.Service
    });
}
