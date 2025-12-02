using FinanceManager.Application;
using FinanceManager.Domain.Notifications;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages user settings for the signed-in account: profile, notification preferences, and import split configuration.
/// </summary>
[ApiController]
[Route("api/user/settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class UserSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly ILogger<UserSettingsController> _logger;

    public UserSettingsController(AppDbContext db, ICurrentUserService current, ILogger<UserSettingsController> logger)
    { _db = db; _current = current; _logger = logger; }

    /// <summary>
    /// Returns profile settings of the current user (language, timezone, API key flags).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>User profile settings DTO.</returns>
    // GET api/user/settings/profile
    [HttpGet("profile")]
    [ProducesResponseType(typeof(UserProfileSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfileAsync(CancellationToken ct)
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
            .SingleOrDefaultAsync(ct) ?? new UserProfileSettingsDto();
        return Ok(dto);
    }

    /// <summary>
    /// Updates profile settings of the current user.
    /// </summary>
    /// <param name="req">Profile update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content response on success.</returns>
    /// <response code="204">Profile settings updated successfully.</response>
    /// <response code="400">Invalid profile update data.</response>
    /// <response code="404">User not found.</response>
    // PUT api/user/settings/profile
    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfileAsync([FromBody] UserProfileSettingsUpdateRequest req, CancellationToken ct)
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

    /// <summary>
    /// Returns notification settings like monthly reminder and holiday region/provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Notification settings DTO.</returns>
    // GET api/user/settings/notifications
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotificationsAsync(CancellationToken ct)
    {
        var uid = _current.UserId;
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new NotificationSettingsDto
            {
                MonthlyReminderEnabled = u.MonthlyReminderEnabled,
                MonthlyReminderHour = u.MonthlyReminderHour,
                MonthlyReminderMinute = u.MonthlyReminderMinute,
                HolidayProvider = u.HolidayProviderKind.ToString(),
                HolidayCountryCode = u.HolidayCountryCode,
                HolidaySubdivisionCode = u.HolidaySubdivisionCode
            })
            .SingleOrDefaultAsync(ct) ?? new NotificationSettingsDto();
        return Ok(dto);
    }

    /// <summary>
    /// Updates notification settings (monthly reminder time and holiday provider/region).
    /// </summary>
    /// <param name="req">Notification settings update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content response on success.</returns>
    /// <response code="204">Notification settings updated successfully.</response>
    /// <response code="400">Invalid notification settings data.</response>
    /// <response code="404">User not found.</response>
    // PUT api/user/settings/notifications
    [HttpPut("notifications")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateNotificationsAsync([FromBody] UserNotificationSettingsUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var uid = _current.UserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user == null) { return NotFound(); }

        if (!Enum.TryParse<HolidayProviderKind>(req.HolidayProvider, ignoreCase: true, out var kind))
        {
            ModelState.AddModelError(nameof(req.HolidayProvider), "Invalid holiday provider.");
            return ValidationProblem(ModelState);
        }

        user.SetNotificationSettings(req.MonthlyReminderEnabled);
        user.SetMonthlyReminderTime(req.MonthlyReminderHour, req.MonthlyReminderMinute);
        user.SetHolidayProvider(kind);
        user.SetHolidayRegion(req.HolidayCountryCode, req.HolidaySubdivisionCode);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Returns import split settings (mode, thresholds and sizes).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Import split settings DTO.</returns>
    // GET api/user/settings/import-split
    [HttpGet("import-split")]
    [ProducesResponseType(typeof(ImportSplitSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetImportSplitAsync(CancellationToken ct)
    {
        var userId = _current.UserId;
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new ImportSplitSettingsDto
            {
                Mode = u.ImportSplitMode,
                MaxEntriesPerDraft = u.ImportMaxEntriesPerDraft,
                MonthlySplitThreshold = u.ImportMonthlySplitThreshold,
                MinEntriesPerDraft = u.ImportMinEntriesPerDraft
            })
            .SingleOrDefaultAsync(ct) ?? new ImportSplitSettingsDto();
        return Ok(dto);
    }

    /// <summary>
    /// Updates import split settings (mode, thresholds and sizes) for the current user.
    /// </summary>
    /// <param name="req">Import split settings update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content response on success.</returns>
    /// <response code="204">Import split settings updated successfully.</response>
    /// <response code="400">Invalid import split settings data.</response>
    /// <response code="404">User not found.</response>
    // PUT api/user/settings/import-split
    [HttpPut("import-split")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateImportSplitAsync([FromBody] ImportSplitSettingsUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }

        if (req.MinEntriesPerDraft > req.MaxEntriesPerDraft)
        {
            ModelState.AddModelError(nameof(req.MinEntriesPerDraft), "MinEntriesPerDraft must be <= MaxEntriesPerDraft.");
            return ValidationProblem(ModelState);
        }

        if (req.Mode == ImportSplitMode.MonthlyOrFixed)
        {
            var thr = req.MonthlySplitThreshold ?? req.MaxEntriesPerDraft;
            if (thr < req.MaxEntriesPerDraft)
            {
                ModelState.AddModelError(nameof(req.MonthlySplitThreshold), "Threshold must be >= MaxEntriesPerDraft");
                return ValidationProblem(ModelState);
            }
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _current.UserId, ct);
        if (user == null) { return NotFound(); }

        try
        {
            user.SetImportSplitSettings(req.Mode, req.MaxEntriesPerDraft, req.MonthlySplitThreshold, req.MinEntriesPerDraft);
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "value", ex.Message);
            return ValidationProblem(ModelState);
        }
    }
}
