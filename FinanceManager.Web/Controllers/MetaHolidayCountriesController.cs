using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/meta/holiday-countries")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MetaHolidayCountriesController : ControllerBase
{
    private static readonly string[] Countries = new[]
    {
        "DE","US","GB","AT","CH","FR","ES","IT","NL","BE","DK","SE","NO","FI","IE","PL","CZ","HU","PT"
    };

    [HttpGet]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(Countries);
}
