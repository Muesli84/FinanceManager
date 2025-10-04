using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using FinanceManager.Infrastructure;
using FinanceManager.Domain;
using FinanceManager.Application;
using System.Globalization;
using FinanceManager.Domain.Postings;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/postings")] 
[Authorize]
public sealed class PostingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private const int MaxTake = 250;

    public PostingsController(AppDbContext db, ICurrentUserService current)
    {
        _db = db; _current = current;
    }

    public sealed record PostingDto(Guid Id, DateTime BookingDate, decimal Amount, PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, Guid SourceId, string? Subject, string? RecipientName, string? Description, SecurityPostingSubType? SecuritySubType, decimal? Quantity, Guid GroupId);

    public sealed record GroupLinksDto(Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId);

    [HttpGet("account/{accountId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostingDto>>> GetAccountPostings(Guid accountId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        try
        {
            take = Math.Clamp(take, 1, MaxTake);
            bool owned = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.OwnerUserId == _current.UserId, ct);
            if (!owned) { return NotFound(); }

            // Base postings (bank) for account
            var postings = _db.Postings.AsNoTracking()
                .Where(p => p.AccountId == accountId && p.Kind == PostingKind.Bank);

            // Filter by date range if specified
            if (from.HasValue)
            {
                var f = from.Value.Date;
                postings = postings.Where(p => p.BookingDate >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); // inclusive upper bound
                postings = postings.Where(p => p.BookingDate < t);
            }

            // Left join statement entries for enrichment BEFORE projection
            var joined = from p in postings
                         join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                         from seOpt in seJoin.DefaultIfEmpty()
                         select new
                         {
                             P = p,
                             Subject = p.Subject ?? seOpt.Subject,
                             Recipient = p.RecipientName ?? seOpt.RecipientName,
                             Description = p.Description ?? seOpt.BookingDescription
                         };

            if (!string.IsNullOrWhiteSpace(q))
            {
                string term = q.Trim();
                string termLower = term.ToLowerInvariant();
                DateTime? dateFilter = TryParseDate(term);
                decimal? amountFilter = TryParseAmount(term);

                joined = joined.Where(x =>
                    (x.Subject != null && EF.Functions.Like(x.Subject.ToLower(), "%" + termLower + "%")) ||
                    (x.Recipient != null && EF.Functions.Like(x.Recipient.ToLower(), "%" + termLower + "%")) ||
                    (x.Description != null && EF.Functions.Like(x.Description.ToLower(), "%" + termLower + "%")) ||
                    (dateFilter != null && x.P.BookingDate >= dateFilter && x.P.BookingDate < dateFilter.Value.AddDays(1)) ||
                    (amountFilter != null && (x.P.Amount == amountFilter || x.P.Amount == -amountFilter))
                );
            }

            // Order BEFORE projection so EF can translate
            var ordered = joined
                .OrderByDescending(x => x.P.BookingDate)
                .ThenByDescending(x => x.P.Id)
                .Skip(skip)
                .Take(take);

            var result = await ordered
                .Select(x => new PostingDto(
                    x.P.Id,
                    x.P.BookingDate,
                    x.P.Amount,
                    x.P.Kind,
                    x.P.AccountId,
                    x.P.ContactId,
                    x.P.SavingsPlanId,
                    x.P.SecurityId,
                    x.P.SourceId,
                    x.Subject,
                    x.Recipient,
                    x.Description,
                    x.P.SecuritySubType,
                    x.P.Quantity,
                    x.P.GroupId))
                .ToListAsync(ct);

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(ex);
        }
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
    public async Task<ActionResult<IReadOnlyList<PostingDto>>> GetContactPostings(Guid contactId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }
        var query = _db.Postings.AsNoTracking()
            .Where(p => p.ContactId == contactId && p.Kind == PostingKind.Contact);

        if (from.HasValue)
        {
            var f = from.Value.Date; query = query.Where(p => p.BookingDate >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.Date.AddDays(1); query = query.Where(p => p.BookingDate < t);
        }

        query = query.OrderByDescending(p => p.BookingDate).ThenByDescending(p => p.Id)
            .Skip(skip).Take(take);

        var result = await (from p in query
                            join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                            from seOpt in seJoin.DefaultIfEmpty()
                            select new PostingDto(
                                p.Id,
                                p.BookingDate,
                                p.Amount,
                                p.Kind,
                                p.AccountId,
                                p.ContactId,
                                p.SavingsPlanId,
                                p.SecurityId,
                                p.SourceId,
                                p.Subject ?? seOpt.Subject,
                                p.RecipientName ?? seOpt.RecipientName,
                                p.Description ?? seOpt.BookingDescription,
                                p.SecuritySubType,
                                p.Quantity,
                                p.GroupId))
            .ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("savings-plan/{planId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostingDto>>> GetSavingsPlanPostings(Guid planId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.SavingsPlans.AsNoTracking().AnyAsync(s => s.Id == planId && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }
        var query = _db.Postings.AsNoTracking()
            .Where(p => p.SavingsPlanId == planId && p.Kind == PostingKind.SavingsPlan);

        if (from.HasValue)
        {
            var f = from.Value.Date; query = query.Where(p => p.BookingDate >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.Date.AddDays(1); query = query.Where(p => p.BookingDate < t);
        }

        query = query.OrderByDescending(p => p.BookingDate).ThenByDescending(p => p.Id)
            .Skip(skip).Take(take);

        var result = await (from p in query
                            join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                            from seOpt in seJoin.DefaultIfEmpty()
                            select new PostingDto(
                                p.Id,
                                p.BookingDate,
                                p.Amount,
                                p.Kind,
                                p.AccountId,
                                p.ContactId,
                                p.SavingsPlanId,
                                p.SecurityId,
                                p.SourceId,
                                p.Subject ?? seOpt.Subject,
                                p.RecipientName ?? seOpt.RecipientName,
                                p.Description ?? seOpt.BookingDescription,
                                p.SecuritySubType,
                                p.Quantity,
                                p.GroupId))
            .ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("security/{securityId:guid}")]
    public async Task<ActionResult<IReadOnlyList<PostingDto>>> GetSecurityPostings(Guid securityId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        bool owned = await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == securityId && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }
        var query = _db.Postings.AsNoTracking()
            .Where(p => p.SecurityId == securityId && p.Kind == PostingKind.Security);

        if (from.HasValue)
        {
            var f = from.Value.Date; query = query.Where(p => p.BookingDate >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.Date.AddDays(1); query = query.Where(p => p.BookingDate < t);
        }

        query = query.OrderByDescending(p => p.BookingDate).ThenByDescending(p => p.Id)
            .Skip(skip).Take(take);

        var result = await (from p in query
                            join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                            from seOpt in seJoin.DefaultIfEmpty()
                            select new PostingDto(
                                p.Id,
                                p.BookingDate,
                                p.Amount,
                                p.Kind,
                                p.AccountId,
                                p.ContactId,
                                p.SavingsPlanId,
                                p.SecurityId,
                                p.SourceId,
                                p.Subject ?? seOpt.Subject,
                                p.RecipientName ?? seOpt.RecipientName,
                                p.Description ?? seOpt.BookingDescription,
                                p.SecuritySubType,
                                p.Quantity,
                                p.GroupId))
            .ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("group/{groupId:guid}")]
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
