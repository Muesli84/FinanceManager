using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Exposes available holiday provider kinds to the client.
/// </summary>
[ApiController]
[Route("api/meta/holiday-providers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MetaHolidayProvidersController : ControllerBase
{
    /// <summary>
    /// Returns a list of provider kind names.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var values = Enum.GetNames(typeof(HolidayProviderKind));
        return Ok(values);
    }
}
