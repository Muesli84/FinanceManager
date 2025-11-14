using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Returns subdivisions/regions for a given holiday provider and country.
/// </summary>
[ApiController]
[Route("api/meta/holiday-subdivisions")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MetaHolidaySubdivisionsController : ControllerBase
{
    private readonly IHolidaySubdivisionService _service;

    /// <summary>
    /// Creates a new instance of <see cref="MetaHolidaySubdivisionsController"/>.
    /// </summary>
    public MetaHolidaySubdivisionsController(IHolidaySubdivisionService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns subdivisions for the requested provider and country code. If provider or country is missing or invalid, returns an empty list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] string provider, [FromQuery] string country, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(provider))
        {
            return Ok(Array.Empty<string>());
        }
        if (!Enum.TryParse<HolidayProviderKind>(provider, ignoreCase: true, out var kind))
        {
            return Ok(Array.Empty<string>());
        }
        var list = await _service.GetSubdivisionsAsync(kind, country, ct);
        return Ok(list);
    }
}

