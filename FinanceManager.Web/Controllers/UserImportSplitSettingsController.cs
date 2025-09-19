using System.ComponentModel.DataAnnotations;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Infrastructure; // AppDbContext
using FinanceManager.Domain.Users; // ImportSplitMode on entity
using System.Linq;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/user/import-split-settings")]
[Authorize]
public sealed class UserImportSplitSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UserImportSplitSettingsController(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ImportSplitSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new ImportSplitSettingsDto
            {
                Mode = u.ImportSplitMode,
                MaxEntriesPerDraft = u.ImportMaxEntriesPerDraft,
                MonthlySplitThreshold = u.ImportMonthlySplitThreshold
            })
            .SingleOrDefaultAsync(ct);

        dto ??= new ImportSplitSettingsDto();
        return Ok(dto);
    }

    public sealed class UpdateRequest
    {
        [Required]
        public ImportSplitMode Mode { get; set; }
        [Range(20, 10000)]
        public int MaxEntriesPerDraft { get; set; }
        public int? MonthlySplitThreshold { get; set; }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }

        if (req.Mode == ImportSplitMode.MonthlyOrFixed)
        {
            var thr = req.MonthlySplitThreshold ?? req.MaxEntriesPerDraft;
            if (thr < req.MaxEntriesPerDraft)
            {
                ModelState.AddModelError(nameof(req.MonthlySplitThreshold), "Threshold must be >= MaxEntriesPerDraft");
                return ValidationProblem(ModelState);
            }
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, ct);
        if (user == null)
        {
            return NotFound();
        }

        try
        {
            user.SetImportSplitSettings(req.Mode, req.MaxEntriesPerDraft, req.MonthlySplitThreshold);
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
