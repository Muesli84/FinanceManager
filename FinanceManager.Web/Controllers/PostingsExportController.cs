using System.Globalization;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Application; // for ICurrentUserService
using FinanceManager.Web.Infrastructure; // added

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/postings")] 
[Authorize]
public sealed class PostingsExportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPostingExportService _exportService;
    private readonly ICurrentUserService _current;
    private readonly IConfiguration _config;

    private const int DefaultMaxRows = 50_000;

    public PostingsExportController(AppDbContext db, IPostingExportService exportService, ICurrentUserService current, IConfiguration config)
    {
        _db = db;
        _exportService = exportService;
        _current = current;
        _config = config;
    }

    [HttpGet("account/{accountId:guid}/export")]
    public Task<IActionResult> ExportAccountAsync(Guid accountId, string format = "csv", DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
        => ExportAsync(PostingKind.Bank, accountId, format, from, to, q, ct);

    [HttpGet("contact/{contactId:guid}/export")]
    public Task<IActionResult> ExportContactAsync(Guid contactId, string format = "csv", DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
        => ExportAsync(PostingKind.Contact, contactId, format, from, to, q, ct);

    [HttpGet("savings-plan/{planId:guid}/export")]
    public Task<IActionResult> ExportSavingsPlanAsync(Guid planId, string format = "csv", DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
        => ExportAsync(PostingKind.SavingsPlan, planId, format, from, to, q, ct);

    [HttpGet("security/{securityId:guid}/export")]
    public Task<IActionResult> ExportSecurityAsync(Guid securityId, string format = "csv", DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
        => ExportAsync(PostingKind.Security, securityId, format, from, to, q, ct);

    private async Task<IActionResult> ExportAsync(PostingKind kind, Guid contextId, string format, DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        if (!TryParseFormat(format, out var exportFormat))
        {
            return Problem(title: "Invalid format", detail: "Supported formats are 'csv' and 'xlsx'.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Max rows config
        var maxRowsStr = _config["Exports:MaxRows"];
        var maxRows = DefaultMaxRows;
        if (!string.IsNullOrWhiteSpace(maxRowsStr) && int.TryParse(maxRowsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cfgMax))
        {
            maxRows = Math.Max(1, cfgMax);
        }

        // Ownership + entity display name for filename
        string displayName;
        var userId = _current.UserId;
        switch (kind)
        {
            case PostingKind.Bank:
                var acc = await _db.Accounts.AsNoTracking().Where(a => a.Id == contextId && a.OwnerUserId == userId).Select(a => new { a.Name }).FirstOrDefaultAsync(ct);
                if (acc == null) { return NotFound(); }
                displayName = acc.Name;
                break;
            case PostingKind.Contact:
                var con = await _db.Contacts.AsNoTracking().Where(c => c.Id == contextId && c.OwnerUserId == userId).Select(c => new { c.Name }).FirstOrDefaultAsync(ct);
                if (con == null) { return NotFound(); }
                displayName = con.Name;
                break;
            case PostingKind.SavingsPlan:
                var sp = await _db.SavingsPlans.AsNoTracking().Where(s => s.Id == contextId && s.OwnerUserId == userId).Select(s => new { s.Name }).FirstOrDefaultAsync(ct);
                if (sp == null) { return NotFound(); }
                displayName = sp.Name;
                break;
            case PostingKind.Security:
                var sec = await _db.Securities.AsNoTracking().Where(s => s.Id == contextId && s.OwnerUserId == userId).Select(s => new { s.Name }).FirstOrDefaultAsync(ct);
                if (sec == null) { return NotFound(); }
                displayName = sec.Name;
                break;
            default:
                return Problem(title: "Unsupported context", statusCode: StatusCodes.Status400BadRequest);
        }

        var query = new PostingExportQuery(
            OwnerUserId: userId,
            ContextKind: kind,
            ContextId: contextId,
            Format: exportFormat,
            MaxRows: maxRows,
            From: from,
            To: to,
            Q: q);

        try
        {
            var safeContext = kind.ToString();
            var safeName = SanitizeFileName(displayName);

            if (exportFormat == PostingExportFormat.Csv)
            {
                var total = await _exportService.CountAsync(query, ct);
                if (total > maxRows)
                {
                    return Problem(title: "Export limit exceeded", detail: $"Maximum rows {maxRows} exceeded.", statusCode: StatusCodes.Status400BadRequest);
                }

                var fileName = $"{safeContext}_{safeName}_{DateTime.UtcNow:yyyyMMddHHmm}.csv";
                var result = new StreamCallbackResult("text/csv; charset=utf-8", async (stream, token) =>
                {
                    await _exportService.StreamCsvAsync(query, stream, token);
                })
                {
                    FileDownloadName = fileName
                };
                return result;
            }
            else
            {
                var (contentType, _, stream) = await _exportService.GenerateAsync(query, ct);
                var ext = "xlsx";
                var fileName = $"{safeContext}_{safeName}_{DateTime.UtcNow:yyyyMMddHHmm}.{ext}";
                return File(stream, contentType, fileName);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        catch (ArgumentOutOfRangeException)
        {
            return Problem(title: "Invalid format", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex) when (ex.Message == "MaxRowsExceeded")
        {
            return Problem(title: "Export limit exceeded", detail: $"Maximum rows {maxRows} exceeded.", statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static bool TryParseFormat(string? format, out PostingExportFormat result)
    {
        result = PostingExportFormat.Csv;
        if (string.IsNullOrWhiteSpace(format)) { return true; }
        var f = format.Trim().ToLowerInvariant();
        if (f == "csv") { result = PostingExportFormat.Csv; return true; }
        if (f == "xlsx") { result = PostingExportFormat.Xlsx; return true; }
        return false;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        // limit length
        if (cleaned.Length > 60) { cleaned = cleaned.Substring(0, 60); }
        // avoid empty
        if (string.IsNullOrWhiteSpace(cleaned)) { cleaned = "_"; }
        return cleaned.Replace(' ', '_');
    }
}
