using FinanceManager.Application;
using FinanceManager.Application.Reports; // export service + time series
using FinanceManager.Domain.Postings; // PostingKind for export and aggregates
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Web.Infrastructure; // StreamCallbackResult
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using IPostingReversalService = FinanceManager.Application.Postings.IPostingReversalService;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides consolidated posting endpoints for querying, exporting and retrieving aggregate time series
/// across accounts, contacts, savings plans and securities for the current user.
/// </summary>
[ApiController]
[Route("api/postings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PostingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IPostingsQueryService _postingsQuery;
    private readonly IPostingExportService _exportService;
    private readonly IConfiguration _config;
    private readonly IPostingTimeSeriesService _series;
    private readonly IPostingReversalService _reversalService;
    private const int MaxTake = 250;
    private const int DefaultMaxRows = 50_000;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostingsController"/>.
    /// </summary>
    /// <param name="db">Database context used to query postings and related entities.</param>
    /// <param name="current">Service providing information about the current user.</param>
    /// <param name="postingsQuery">Service used to query postings for different contexts.</param>
    /// <param name="exportService">Service used to export postings to CSV/XLSX formats.</param>
    /// <param name="config">Configuration used to read export limits and options.</param>
    /// <param name="series">Service providing aggregate time series data for postings.</param>
    /// <param name="reversalService">Service used to reverse postings.</param>
    public PostingsController(AppDbContext db, ICurrentUserService current, IPostingsQueryService postingsQuery, IPostingExportService exportService, IConfiguration config, IPostingTimeSeriesService series, IPostingReversalService reversalService)
    { _db = db; _current = current; _postingsQuery = postingsQuery; _exportService = exportService; _config = config; _series = series; _reversalService = reversalService; }

    /// <summary>
    /// Gets a single posting by identifier including linked posting and group bank posting metadata.
    /// </summary>
    /// <param name="id">Posting id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the posting details.</response>
    /// <response code="404">Posting not found.</response>
    /// <returns>The result.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PostingServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostingServiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var p = await _db.Postings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) { return NotFound(); }

        bool owned = false;
        if (p.AccountId.HasValue) { owned |= await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == p.AccountId.Value && a.OwnerUserId == _current.UserId, ct); }
        if (!owned && p.ContactId.HasValue) { owned |= await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == p.ContactId.Value && c.OwnerUserId == _current.UserId, ct); }
        if (!owned && p.SavingsPlanId.HasValue) { owned |= await _db.SavingsPlans.AsNoTracking().AnyAsync(s => s.Id == p.SavingsPlanId.Value && s.OwnerUserId == _current.UserId, ct); }
        if (!owned && p.SecurityId.HasValue) { owned |= await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == p.SecurityId.Value && s.OwnerUserId == _current.UserId, ct); }
        if (!owned) { return NotFound(); }

        var se = await _db.StatementEntries.AsNoTracking().FirstOrDefaultAsync(se => se.Id == p.SourceId, ct);

        // linked posting metadata
        Guid? linkedId = p.LinkedPostingId;
        PostingKind? lkind = null; Guid? lacc = null; Guid? laccSym = null; string? laccName = null;
        if (linkedId != null)
        {
            var lp = await _db.Postings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkedId, ct);
            if (lp != null)
            {
                lkind = lp.Kind;
                lacc = lp.AccountId;
                if (lp.AccountId != null)
                {
                    var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == lp.AccountId.Value, ct);
                    if (acc != null)
                    {
                        laccSym = acc.SymbolAttachmentId;
                        laccName = acc.Name;
                    }
                }
            }
        }

        // bank posting for this posting's group
        Guid? bankAccId = null; Guid? bankAccSym = null; string? bankAccName = null;
        if (p.GroupId != Guid.Empty)
        {
            var bp = await _db.Postings.AsNoTracking().Where(x => x.GroupId == p.GroupId && x.Kind == PostingKind.Bank).FirstOrDefaultAsync(ct);
            if (bp != null && bp.AccountId != null)
            {
                bankAccId = bp.AccountId;
                var acc2 = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == bp.AccountId.Value, ct);
                if (acc2 != null)
                {
                    bankAccSym = acc2.SymbolAttachmentId;
                    bankAccName = acc2.Name;
                    if (bankAccSym is null)
                    {
                        var cont = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == p.ContactId, ct);
                        if (cont != null) { bankAccSym = cont.SymbolAttachmentId; }
                    }
                }
            }
        }

        var dto = new PostingServiceDto(
            p.Id,
            p.BookingDate,
            p.ValutaDate,
            p.Amount,
            p.Kind,
            p.AccountId,
            p.ContactId,
            p.SavingsPlanId,
            p.SecurityId,
            p.SourceId,
            p.Subject ?? se?.Subject,
            p.RecipientName ?? se?.RecipientName,
            p.Description ?? se?.BookingDescription,
            p.SecuritySubType,
            p.Quantity,
            p.GroupId,
            linkedId,
            lkind,
            lacc,
            laccSym,
            laccName,
            bankAccId,
            bankAccSym,
            bankAccName,
            p.IsReversed,
            p.IsReversal,
            p.ReversedByPostingId,
            p.ReversalForPostingId,
            p.IsPreliminary);
        return Ok(dto);
    }

    /// <summary>
    /// Reverses a posting by creating a counter-posting with negated amount.
    /// </summary>
    /// <param name="id">Posting id to reverse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the reversal result.</response>
    /// <response code="400">Posting not found or cannot be reversed.</response>
    /// <response code="403">User is not authorized to reverse this posting.</response>
    /// <response code="409">Posting is already reversed.</response>
    /// <returns>The result.</returns>
    [HttpPost("{id:guid}/reverse")]
    [ProducesResponseType(typeof(ReversalResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReversalResultDto>> ReversePosting(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _reversalService.ReversePostingAsync(id, _current.UserId, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Problem(title: "Forbidden", detail: "You are not authorized to reverse this posting.", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already been reversed") || ex.Message.Contains("already reversed"))
        {
            return Problem(title: "Conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Bad Request", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Validates whether a posting can be reversed.
    /// </summary>
    /// <param name="id">Posting id to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the validation result.</response>
    /// <returns>The result.</returns>
    [HttpGet("{id:guid}/validate-reversal")]
    [ProducesResponseType(typeof(ReversalValidationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReversalValidationDto>> ValidateReversal(Guid id, CancellationToken ct)
    {
        var result = await _reversalService.CanReverseAsync(id, _current.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lists postings for an account with optional paging, search and date filters.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="skip">Number of records to skip (for pagination).</param>
    /// <param name="take">Number of records to take (max 250).</param>
    /// <param name="q">Optional search query.</param>
    /// <param name="from">Optional start date for filtering.</param>
    /// <param name="to">Optional end date for filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the list of postings.</response>
    /// <response code="404">Account not found or no postings.</response>
    /// <returns>The result.</returns>
    [HttpGet("account/{accountId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PostingServiceDto>>> GetAccountPostings(Guid accountId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        var rows = await _postingsQuery.GetAccountPostingsAsync(accountId, skip, take, q, from, to, _current.UserId, ct);
        return Ok(rows);
    }

    private static DateTime? TryParseDate(string input)
    {
        string[] formats = { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { return dt.Date; }
        if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt)) { return dt.Date; }
        return null;
    }

    private static decimal? TryParseAmount(string input)
    {
        var norm = input.Replace(" ", string.Empty).Replace("�", string.Empty);
        if (decimal.TryParse(norm, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var dec)) { return Math.Abs(dec); }
        if (decimal.TryParse(norm, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out dec)) { return Math.Abs(dec); }
        return null;
    }

    /// <summary>
    /// Lists postings for a contact with optional paging, search and date filters.
    /// </summary>
    /// <param name="contactId">The contact id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="q">The q.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("contact/{contactId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PostingServiceDto>>> GetContactPostings(Guid contactId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        var rows = await _postingsQuery.GetContactPostingsAsync(contactId, skip, take, q, from, to, _current.UserId, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Lists postings for a savings plan with optional paging and filters.
    /// </summary>
    /// <param name="planId">The plan id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="q">The q.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("savings-plan/{planId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PostingServiceDto>>> GetSavingsPlanPostings(Guid planId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.SavingsPlans.AsNoTracking().AnyAsync(s => s.Id == planId && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        var rows = await _postingsQuery.GetSavingsPlanPostingsAsync(planId, skip, take, q, from, to, _current.UserId, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Lists postings for a security with optional paging and date filters.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("security/{securityId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PostingServiceDto>>> GetSecurityPostings(Guid securityId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == securityId && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        var rows = await _postingsQuery.GetSecurityPostingsAsync(securityId, skip, take, from, to, _current.UserId, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Returns group linkage (first account/contact/savings/security id) for a posting group.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("group/{groupId:guid}")]
    [ProducesResponseType(typeof(GroupLinksDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupLinksDto>> GetGroupLinksAsync(Guid groupId, CancellationToken ct)
    {
        if (groupId == Guid.Empty) { return BadRequest(); }

        var baseQuery = _db.Postings.AsNoTracking().Where(p => p.GroupId == groupId);
        // Collect candidate ids
        var accountIds = await baseQuery.Select(p => p.AccountId).Where(id => id != null).Select(id => id!.Value).Distinct().ToListAsync(ct);
        var contactIds = await baseQuery.Select(p => p.ContactId).Where(id => id != null).Select(id => id!.Value).Distinct().ToListAsync(ct);
        var planIds = await baseQuery.Select(p => p.SavingsPlanId).Where(id => id != null).Select(id => id!.Value).Distinct().ToListAsync(ct);
        var securityIds = await baseQuery.Select(p => p.SecurityId).Where(id => id != null).Select(id => id!.Value).Distinct().ToListAsync(ct);

        // Ownership guard: ensure at least one entity of the group belongs to current user
        var anyOwned =
            (accountIds.Count > 0 && await _db.Accounts.AsNoTracking().AnyAsync(a => accountIds.Contains(a.Id) && a.OwnerUserId == _current.UserId, ct)) ||
            (contactIds.Count > 0 && await _db.Contacts.AsNoTracking().AnyAsync(c => contactIds.Contains(c.Id) && c.OwnerUserId == _current.UserId, ct)) ||
            (planIds.Count > 0 && await _db.SavingsPlans.AsNoTracking().AnyAsync(s => planIds.Contains(s.Id) && s.OwnerUserId == _current.UserId, ct)) ||
            (securityIds.Count > 0 && await _db.Securities.AsNoTracking().AnyAsync(s => securityIds.Contains(s.Id) && s.OwnerUserId == _current.UserId, ct));

        if (!anyOwned) { return NotFound(); }

        var dto = new GroupLinksDto(
            accountIds.FirstOrDefault(),
            contactIds.FirstOrDefault(),
            planIds.FirstOrDefault(),
            securityIds.FirstOrDefault());
        return Ok(dto);
    }

    /// <summary>
    /// Exports postings for an account in CSV or XLSX format.
    /// </summary>
    /// <param name="accountId">The account id.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("account/{accountId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportAccountAsync(Guid accountId, [FromQuery] PostingExportRequest req, CancellationToken ct = default)
        => ExportAsync(PostingKind.Bank, accountId, req, ct);

    /// <summary>
    /// Exports postings for a contact in CSV or XLSX format.
    /// </summary>
    /// <param name="contactId">The contact id.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("contact/{contactId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportContactAsync(Guid contactId, [FromQuery] PostingExportRequest req, CancellationToken ct = default)
        => ExportAsync(PostingKind.Contact, contactId, req, ct);

    /// <summary>
    /// Exports postings for a savings plan in CSV or XLSX format.
    /// </summary>
    /// <param name="planId">The plan id.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("savings-plan/{planId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportSavingsPlanAsync(Guid planId, [FromQuery] PostingExportRequest req, CancellationToken ct = default)
        => ExportAsync(PostingKind.SavingsPlan, planId, req, ct);

    /// <summary>
    /// Exports postings for a security in CSV or XLSX format.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("security/{securityId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportSecurityAsync(Guid securityId, [FromQuery] PostingExportRequest req, CancellationToken ct = default)
        => ExportAsync(PostingKind.Security, securityId, req, ct);

    /// <summary>
    /// Performs the export operation for the specified context and returns a file result.
    /// The method validates the requested format, enforces configured row limits and delegates the actual export to <see cref="IPostingExportService"/>.
    /// </summary>
    /// <param name="kind">Context kind for the export (account/contact/savings-plan/security).</param>
    /// <param name="contextId">Identifier of the context entity.</param>
    /// <param name="req">Export request containing format and optional filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> that either contains a file download or an appropriate Problem/NotFound result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the requested format is invalid.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown by the export service when the context is not accessible.</exception>
    private async Task<IActionResult> ExportAsync(PostingKind kind, Guid contextId, PostingExportRequest req, CancellationToken ct)
    {
        if (!TryParseFormat(req.Format, out var exportFormat))
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
            From: req.From,
            To: req.To,
            Q: req.Q);

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
                var fileName = $"{safeContext}_{safeName}_{DateTime.UtcNow:yyyyMMddHHmm}.xlsx";
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
        if (cleaned.Length > 60) { cleaned = cleaned.Substring(0, 60); }
        if (string.IsNullOrWhiteSpace(cleaned)) { cleaned = "_"; }
        return cleaned.Replace(' ', '_');
    }

    /// <summary>
    /// Returns aggregate time series for a single account.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="period">Optional period filter (default: Month).</param>
    /// <param name="take">Optional limit on number of periods (default: 36).</param>
    /// <param name="maxYearsBack">Optional maximum years back (default: null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the aggregate data.</response>
    /// <response code="404">Account not found.</response>
    /// <returns>The result.</returns>
    [HttpGet("~/api/accounts/{accountId:guid}/aggregates")]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAccountAggregates(Guid accountId, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => HandleEntityAsync(PostingKind.Bank, accountId, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns aggregate time series across all accounts.
    /// </summary>
    /// <param name="period">Optional period filter (default: Month).</param>
    /// <param name="take">Optional limit on number of periods (default: 36).</param>
    /// <param name="maxYearsBack">Optional maximum years back (default: null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the aggregate data.</response>
    /// <returns>The result.</returns>
    [HttpGet("~/api/accounts/aggregates")]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAccountsAllAggregates([FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => HandleAllAsync(PostingKind.Bank, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns aggregate time series for a single contact.
    /// </summary>
    /// <param name="contactId">Contact identifier.</param>
    /// <param name="period">Optional period filter (default: Month).</param>
    /// <param name="take">Optional limit on number of periods (default: 36).</param>
    /// <param name="maxYearsBack">Optional maximum years back (default: null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the aggregate data.</response>
    /// <response code="404">Contact not found.</response>
    /// <returns>The result.</returns>
    [HttpGet("~/api/contacts/{contactId:guid}/aggregates")]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetContactAggregates(Guid contactId, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => HandleEntityAsync(PostingKind.Contact, contactId, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns aggregate time series for a single savings plan.
    /// </summary>
    /// <param name="planId">Savings plan identifier.</param>
    /// <param name="period">Optional period filter (default: Month).</param>
    /// <param name="take">Optional limit on number of periods (default: 36).</param>
    /// <param name="maxYearsBack">Optional maximum years back (default: null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the aggregate data.</response>
    /// <response code="404">Savings plan not found.</response>
    /// <returns>The result.</returns>
    [HttpGet("~/api/savings-plans/{planId:guid}/aggregates")]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetSavingsPlanAggregates(Guid planId, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => HandleEntityAsync(PostingKind.SavingsPlan, planId, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns aggregate time series across all savings plans.
    /// </summary>
    /// <param name="period">Optional period filter (default: Month).</param>
    /// <param name="take">Optional limit on number of periods (default: 36).</param>
    /// <param name="maxYearsBack">Optional maximum years back (default: null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the aggregate data.</response>
    /// <returns>The result.</returns>
    [HttpGet("~/api/savings-plans/aggregates")]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetSavingsPlansAllAggregates([FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => HandleAllAsync(PostingKind.SavingsPlan, period, take, maxYearsBack, ct);

    private static int? NormalizeYears(int? maxYearsBack) { if (!maxYearsBack.HasValue) return null; return Math.Clamp(maxYearsBack.Value, 1, 10); }
    private static AggregatePeriod ParsePeriod(string period) { if (!Enum.TryParse<AggregatePeriod>(period, true, out var p)) { p = AggregatePeriod.Month; } return p; }
    private static int NormalizeTake(AggregatePeriod p, int take) { var def = p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10; return Math.Clamp(take <= 0 ? def : take, 1, 200); }

    private async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> HandleEntityAsync(PostingKind kind, Guid entityId, string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        var p = ParsePeriod(period); take = NormalizeTake(p, take); var years = NormalizeYears(maxYearsBack);
        var data = await _series.GetAsync(_current.UserId, kind, entityId, p, take, years, ct);
        if (data == null) return NotFound();
        return Ok(data.Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList());
    }

    private async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> HandleAllAsync(PostingKind kind, string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        var p = ParsePeriod(period); take = NormalizeTake(p, take); var years = NormalizeYears(maxYearsBack);
        var data = await _series.GetAllAsync(_current.UserId, kind, p, take, years, ct);
        return Ok(data.Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList());
    }
}
