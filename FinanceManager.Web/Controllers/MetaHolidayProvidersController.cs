using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/meta/holiday-providers")]
[Authorize]
public sealed class MetaHolidayProvidersController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var values = Enum.GetNames(typeof(HolidayProviderKind));
        return Ok(values);
    }
}
