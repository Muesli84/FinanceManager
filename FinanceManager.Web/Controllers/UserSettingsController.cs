using FinanceManager.Application;
using FinanceManager.Application.Common;
using FinanceManager.Domain.Notifications;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Web.Infrastructure.ApiErrors;
using FinanceManager.Web.Infrastructure.Auth;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages user settings for the signed-in account: profile, notification preferences, and import split configuration.
/// All endpoints operate on the currently authenticated user resolved via <see cref="ICurrentUserService"/>.
/// </summary>
[ApiController]
[Route("api/user/settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class UserSettingsController : ControllerBase
{
    private const string Origin = "API_UserSettings";
    private const string AuthCookieName = "FinanceManager.Auth";

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly ILogger<UserSettingsController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;
    private readonly IJwtTokenService _jwt;
    private readonly IAuthTokenProvider _tokenProvider;
    private readonly UserManager<User> _userManager;
    private readonly IAlphaVantageSecretProtector _alphaVantageSecretProtector;

    /// <summary>
    /// Initializes a new instance of <see cref="UserSettingsController"/>
    /// </summary>
    /// <param name="db">Database context used to access user settings.</param>
    /// <param name="current">Service that exposes current user information.</param>
    /// <param name="logger">Logger instance for diagnostics and error reporting.</param>
    /// <param name="localizer">Localizer for accessing localized strings.</param>
    /// <param name="jwt">Service used to issue new JWT tokens after profile changes.</param>
    /// <param name="tokenProvider">Token provider whose cache is invalidated after the auth cookie is replaced.</param>
    /// <param name="userManager">Identity user manager used to read current roles and security stamp.</param>
    /// <param name="alphaVantageSecretProtector">Protector used to store AlphaVantage API keys in encrypted form.</param>
    public UserSettingsController(AppDbContext db, ICurrentUserService current, ILogger<UserSettingsController> logger, IStringLocalizer<Controller> localizer, IJwtTokenService jwt, IAuthTokenProvider tokenProvider, UserManager<User> userManager, IAlphaVantageSecretProtector alphaVantageSecretProtector)
    {
        _db = db;
        _current = current;
        _logger = logger;
        _localizer = localizer;
        _jwt = jwt;
        _tokenProvider = tokenProvider;
        _userManager = userManager;
        _alphaVantageSecretProtector = alphaVantageSecretProtector;
    }

    /// <summary>
    /// Returns profile settings of the current user (language, timezone, API key flags).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>User profile settings DTO wrapped in an <see cref="IActionResult"/> (200 OK).</returns>
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
                ShareAlphaVantageApiKey = u.ShareAlphaVantageApiKey,
                CacheKpisInLocalStorage = u.CacheKpisInLocalStorage,
                ShowConfirmations = u.ShowConfirmations
            })
            .SingleOrDefaultAsync(ct) ?? new UserProfileSettingsDto();
        return Ok(dto);
    }

    /// <summary>
    /// Updates profile settings of the current user.
    /// </summary>
    /// <param name="req">Profile update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content response on success or validation/problem responses on failure.</returns>
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
            var languageChanged = req.PreferredLanguage != user.PreferredLanguage;
            var timezoneChanged = req.TimeZoneId != user.TimeZoneId;

            user.SetPreferredLanguage(req.PreferredLanguage);
            user.SetTimeZoneId(req.TimeZoneId);
            user.SetCacheKpisInLocalStorage(req.CacheKpisInLocalStorage);
            user.SetShowConfirmations(req.ShowConfirmations);

            if (req.ClearAlphaVantageApiKey == true)
            {
                user.SetAlphaVantageKey(null);
            }
            else if (!string.IsNullOrWhiteSpace(req.AlphaVantageApiKey))
            {
                user.SetAlphaVantageKey(_alphaVantageSecretProtector.Protect(req.AlphaVantageApiKey));
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

            // Re-issue the auth cookie whenever language or timezone changed so the new pref_lang/tz
            // claims are picked up immediately by the request culture provider without requiring re-login.
            if (languageChanged || timezoneChanged)
            {
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                var securityStamp = await _userManager.GetSecurityStampAsync(user);
                var newToken = _jwt.CreateToken(user.Id, user.UserName!, isAdmin, securityStamp, out var expiresUtc, user.PreferredLanguage, user.TimeZoneId);
                Response.Cookies.Append(AuthCookieName, newToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    IsEssential = true,
                    Expires = new DateTimeOffset(expiresUtc)
                });
                _tokenProvider.InvalidateCache();
                _logger.LogInformation("Reissued auth cookie after profile language/timezone update for user {UserId}", user.Id);
            }

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
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Returns notification settings like monthly reminder and holiday region/provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Notification settings DTO wrapped in an <see cref="IActionResult"/> (200 OK).</returns>
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
    /// <returns>No content response on success or validation responses on failure.</returns>
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
    /// <returns>Import split settings DTO wrapped in an <see cref="IActionResult"/> (200 OK).</returns>
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
                MinEntriesPerDraft = u.ImportMinEntriesPerDraft,
                MassImportDialogPolicy = u.MassImportDialogPolicy,
                KnownContactAutoCreateEnabled = u.KnownContactAutoCreateEnabled
            })
            .SingleOrDefaultAsync(ct) ?? new ImportSplitSettingsDto();
        return Ok(dto);
    }

    /// <summary>
    /// Updates import split settings (mode, thresholds and sizes) for the current user.
    /// </summary>
    /// <param name="req">Import split settings update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content response on success or validation responses on failure.</returns>
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
            user.SetMassImportDialogPolicy(req.MassImportDialogPolicy);
            user.SetKnownContactAutoCreateEnabled(req.KnownContactAutoCreateEnabled);
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
