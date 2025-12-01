using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/meta")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class MetaHolidaysController : ControllerBase
{
    private readonly IHolidaySubdivisionService _service;

    private static readonly string[] Countries = new[]
    {
        "DE","US","GB","AT","CH","FR","ES","IT","NL","BE","DK","SE","NO","FI","IE","PL","CZ","HU","PT"
    };

    public MetaHolidaysController(IHolidaySubdivisionService service)
    {
        _service = service;
    }

    // GET api/meta/holiday-providers
    [HttpGet("holiday-providers")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult GetProviders()
    {
        var values = Enum.GetNames(typeof(HolidayProviderKind));
        return Ok(values);
    }

    // GET api/meta/holiday-countries
    [HttpGet("holiday-countries")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult GetCountries() => Ok(Countries);

    // GET api/meta/holiday-subdivisions?provider=...&country=...
    [HttpGet("holiday-subdivisions")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubdivisions([FromQuery] string provider, [FromQuery] string country, CancellationToken ct)
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
