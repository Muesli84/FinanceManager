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

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync([FromBody] UserProfileSettingsUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _current.UserId, ct);
        if (user == null) return NotFound();
        try
        {
            user.SetPreferredLanguage(req.PreferredLanguage);
            user.SetTimeZoneId(req.TimeZoneId);

            if (req.ClearAlphaVantageApiKey == true)
            {
                user.SetAlphaVantageKey(null);
            }
            else if (!string.IsNullOrWhiteSpace(req.AlphaVantageApiKey))
            {
                user.SetAlphaVantageKey(req.AlphaVantageApiKey);
            }

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
