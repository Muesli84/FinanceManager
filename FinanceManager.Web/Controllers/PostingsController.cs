using FinanceManager.Application;
using FinanceManager.Infrastructure;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/postings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PostingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IPostingsQueryService _postingsQuery;
    private const int MaxTake = 250;

    public PostingsController(AppDbContext db, ICurrentUserService current, IPostingsQueryService postingsQuery)
    {
        _db = db; _current = current; _postingsQuery = postingsQuery;
    }

    // Added ValutaDate to DTO
    public sealed record PostingDto(Guid Id, DateTime BookingDate, DateTime ValutaDate, decimal Amount, PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, Guid SourceId, string? Subject, string? RecipientName, string? Description, SecurityPostingSubType? SecuritySubType, decimal? Quantity, Guid GroupId, Guid? LinkedPostingId, PostingKind? LinkedPostingKind, Guid? LinkedPostingAccountId, Guid? LinkedPostingAccountSymbolAttachmentId, string? LinkedPostingAccountName, Guid? BankPostingAccountId, Guid? BankPostingAccountSymbolAttachmentId, string? BankPostingAccountName);

    public sealed record GroupLinksDto(Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PostingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostingDto>> GetById(Guid id, CancellationToken ct)
    {
        var p = await _db.Postings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) { return NotFound(); }

        bool owned = false;
        if (p.AccountId.HasValue)
        {
            owned |= await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == p.AccountId.Value && a.OwnerUserId == _current.UserId, ct);
        }
        if (!owned && p.ContactId.HasValue)
        {
            owned |= await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == p.ContactId.Value && c.OwnerUserId == _current.UserId, ct);
        }
        if (!owned && p.SavingsPlanId.HasValue)
        {
            owned |= await _db.SavingsPlans.AsNoTracking().AnyAsync(s => s.Id == p.SavingsPlanId.Value && s.OwnerUserId == _current.UserId, ct);
        }
        if (!owned && p.SecurityId.HasValue)
        {
            owned |= await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == p.SecurityId.Value && s.OwnerUserId == _current.UserId, ct);
        }
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
                        if (cont != null)
                        {
                            bankAccSym = cont.SymbolAttachmentId;
                        }
                    }
                }
            }
        }

        var dto = new PostingDto(
            p.Id,
            p.BookingDate,
            p.ValutaDate, // <- include valuta
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
            bankAccName);
        return Ok(dto);
    }

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
        var norm = input.Replace(" ", string.Empty).Replace("€", string.Empty);
        if (decimal.TryParse(norm, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var dec)) { return Math.Abs(dec); }
        if (decimal.TryParse(norm, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out dec)) { return Math.Abs(dec); }
        return null;
    }

    [HttpGet("contact/{contactId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PostingServiceDto>>> GetContactPostings(Guid contactId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        // Use postings query service to load contact postings (shared DTO)
        var rows = await _postingsQuery.GetContactPostingsAsync(contactId, skip, take, q, from, to, _current.UserId, ct);
        return Ok(rows);
    }

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

        if (!anyOwned)
        {
            return NotFound();
        }

        var dto = new GroupLinksDto(
            accountIds.FirstOrDefault(),
            contactIds.FirstOrDefault(),
            planIds.FirstOrDefault(),
            securityIds.FirstOrDefault());
        return Ok(dto);
    }
}
