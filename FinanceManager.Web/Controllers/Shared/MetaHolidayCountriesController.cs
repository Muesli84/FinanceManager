using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Provides a small list of supported holiday country codes used by the client for region selection.
/// </summary>
[ApiController]
[Route("api/meta/holiday-countries")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MetaHolidayCountriesController : ControllerBase
{
    private static readonly string[] Countries = new[]
    {
        "DE","US","GB","AT","CH","FR","ES","IT","NL","BE","DK","SE","NO","FI","IE","PL","CZ","HU","PT"
    };

    /// <summary>
    /// Returns the list of supported country codes for holiday lookups.
    /// </summary>
    /// <returns>An array of ISO country codes.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(Countries);
}

