using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides a lightweight health endpoint used by monitoring and the update subsystem.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Returns a static <c>ok</c> status payload.
    /// </summary>
    /// <returns>An object with a <c>status</c> property set to <c>ok</c>.</returns>
    /// <response code="200">The service is healthy.</response>
    [HttpGet("health")]
    [HttpGet("api/health")]
    public IActionResult Get() => Ok(new { status = "ok" });
}
