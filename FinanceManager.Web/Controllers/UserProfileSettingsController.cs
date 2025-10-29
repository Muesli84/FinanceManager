using FinanceManager.Application;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/user/profile-settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class UserProfileSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly ILogger<UserProfileSettingsController> _logger;

    public UserProfileSettingsController(AppDbContext db, ICurrentUserService current, ILogger<UserProfileSettingsController> logger)
    { _db = db; _current = current; _logger = logger; }

    [HttpGet]
    [ProducesResponseType(typeof(UserProfileSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var uid = _current.UserId;
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new UserProfileSettingsDto
            {
                PreferredLanguage = u.PreferredLanguage,
                TimeZoneId = u.TimeZoneId,
                HasAlphaVantageApiKey = u.AlphaVantageApiKey != null,
                ShareAlphaVantageApiKey = u.ShareAlphaVantageApiKey
            })
            .SingleOrDefaultAsync(ct);
        dto ??= new();
        return Ok(dto);
    }

    public sealed class UpdateRequest
    {
        [MaxLength(10)] public string? PreferredLanguage { get; set; }
        [MaxLength(100)] public string? TimeZoneId { get; set; }

        // New: AlphaVantage key controls
        [MaxLength(120)] public string? AlphaVantageApiKey { get; set; }  // optional: set/replace
        public bool? ClearAlphaVantageApiKey { get; set; }                // optional: clear
        public bool? ShareAlphaVantageApiKey { get; set; }                // optional: admin-only
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _current.UserId, ct);
        if (user == null) return NotFound();
        try
        {
            user.SetPreferredLanguage(req.PreferredLanguage);
            user.SetTimeZoneId(req.TimeZoneId);

            // AlphaVantage key set/clear
            if (req.ClearAlphaVantageApiKey == true)
            {
                user.SetAlphaVantageKey(null);
            }
            else if (!string.IsNullOrWhiteSpace(req.AlphaVantageApiKey))
            {
                user.SetAlphaVantageKey(req.AlphaVantageApiKey);
            }

            // Share flag: only users in Admin role may enable/disable sharing
            if (req.ShareAlphaVantageApiKey.HasValue)
            {
                if (!_current.IsAdmin && req.ShareAlphaVantageApiKey.Value)
                {
                    return Forbid();
                }
                if (_current.IsAdmin)
                {
                    user.SetShareAlphaVantageKey(req.ShareAlphaVantageApiKey.Value);
                }
            }

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "value", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update profile settings failed for {UserId}", _current.UserId);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
