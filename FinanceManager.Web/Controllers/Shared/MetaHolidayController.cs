using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers.Shared
{
    /// <summary>
    /// Consolidated endpoints for holiday metadata used by the client (countries, providers, subdivisions).
    /// This merges the previous small controllers into a single controller to reduce duplication and keep
    /// related routes in one place.
    /// </summary>
    [ApiController]
    [Route("api/meta")]
    [Produces(MediaTypeNames.Application.Json)]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public sealed class MetaHolidayController : ControllerBase
    {
        private static readonly string[] Countries = new[]
        {
            "DE","US","GB","AT","CH","FR","ES","IT","NL","BE","DK","SE","NO","FI","IE","PL","CZ","HU","PT"
        };

        private readonly IHolidaySubdivisionService _subdivisionService;

        public MetaHolidayController(IHolidaySubdivisionService subdivisionService)
        {
            _subdivisionService = subdivisionService;
        }

        /// <summary>
        /// Returns the list of supported country codes for holiday lookups.
        /// </summary>
        /// <returns>An array of ISO country codes.</returns>
        [HttpGet("holiday-countries")]
        [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
        public IActionResult GetCountries() => Ok(Countries);

        /// <summary>
        /// Returns a list of available holiday provider kinds.
        /// </summary>
        [HttpGet("holiday-providers")]
        [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
        public IActionResult GetProviders()
        {
            var values = Enum.GetNames(typeof(HolidayProviderKind));
            return Ok(values);
        }

        /// <summary>
        /// Returns subdivisions for the requested provider and country code.
        /// If provider or country is missing or invalid, returns an empty list.
        /// </summary>
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

            var list = await _subdivisionService.GetSubdivisionsAsync(kind, country, ct);
            return Ok(list);
        }
    }
}
