using FinanceManager.Application;
using FinanceManager.Domain.Notifications;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/user/notification-settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class UserNotificationSettingsController : ControllerBase
{
    private readonly FinanceManager.Infrastructure.AppDbContext _db;
    private readonly ICurrentUserService _current;

    public UserNotificationSettingsController(FinanceManager.Infrastructure.AppDbContext db, ICurrentUserService current)
    {
        _db = db; _current = current;
    }

    [HttpGet]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var uid = _current.UserId;
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new NotificationSettingsDto {
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

    public sealed class UpdateRequest
    {
        public bool MonthlyReminderEnabled { get; set; }
        [Range(0,23)] public int? MonthlyReminderHour { get; set; }
        [Range(0,59)] public int? MonthlyReminderMinute { get; set; }
        [Required] public string HolidayProvider { get; set; } = "Memory";
        [StringLength(10, MinimumLength = 2)] public string? HolidayCountryCode { get; set; }
        [StringLength(20, MinimumLength = 2)] public string? HolidaySubdivisionCode { get; set; }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateRequest req, CancellationToken ct)
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
}
