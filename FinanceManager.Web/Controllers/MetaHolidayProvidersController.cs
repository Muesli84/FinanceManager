using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides metadata endpoints for holiday provider info: providers, supported countries and subdivisions.
/// Used for configuring notification settings.
/// </summary>
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

    /// <summary>
    /// Returns available holiday provider kinds.
    /// </summary>
    // GET api/meta/holiday-providers
    [HttpGet("holiday-providers")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult GetProviders()
    {
        var values = Enum.GetNames(typeof(HolidayProviderKind));
        return Ok(values);
    }

    /// <summary>
    /// Returns the list of supported country ISO codes for holiday data.
    /// </summary>
    // GET api/meta/holiday-countries
    [HttpGet("holiday-countries")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult GetCountries() => Ok(Countries);

    /// <summary>
    /// Returns subdivision (state / region) codes for a given provider + country combination.
    /// </summary>
    /// <param name="provider">Provider kind (enum name, case insensitive).</param>
    /// <param name="country">ISO country code.</param>
    /// <param name="ct">Cancellation token.</param>
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
