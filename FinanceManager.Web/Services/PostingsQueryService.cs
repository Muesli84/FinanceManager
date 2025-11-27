using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;
using FinanceManager.Domain;

namespace FinanceManager.Web.Services
{
    public class PostingsQueryService : IPostingsQueryService
    {
        private readonly AppDbContext _db;
        public PostingsQueryService(AppDbContext db) { _db = db; }

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

        public async Task<IReadOnlyList<PostingServiceDto>> GetContactPostingsAsync(Guid contactId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 250);
            bool owned = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == currentUserId, ct);
            if (!owned) return Array.Empty<PostingServiceDto>();

            var baseQuery = _db.Postings.AsNoTracking().Where(p => p.ContactId == contactId && p.Kind == PostingKind.Contact);

            if (from.HasValue)
            {
                var f = from.Value.Date; baseQuery = baseQuery.Where(p => p.BookingDate >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); baseQuery = baseQuery.Where(p => p.BookingDate < t);
            }

            var joined = from p in baseQuery
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

            var ordered = joined.OrderByDescending(x => x.P.ValutaDate).ThenByDescending(x => x.P.BookingDate).ThenByDescending(x => x.P.Id).Skip(skip).Take(take);

            // Define an intermediate projection type to avoid EF translation issues with enum casts
            var queryProjected = from x in ordered
                                join lp in _db.Postings.AsNoTracking() on x.P.LinkedPostingId equals lp.Id into lpJoin
                                from lpOpt in lpJoin.DefaultIfEmpty()
                                join bp in _db.Postings.AsNoTracking().Where(b => b.Kind == PostingKind.Bank) on x.P.GroupId equals bp.GroupId into bpJoin
                                from bpOpt in bpJoin.DefaultIfEmpty()
                                join bpAcc in _db.Accounts.AsNoTracking() on bpOpt.AccountId equals bpAcc.Id into bpAccJoin
                                from bpAccOpt in bpAccJoin.DefaultIfEmpty()
                                join lpBp in _db.Postings.AsNoTracking().Where(b => b.Kind == PostingKind.Bank) on lpOpt.GroupId equals lpBp.GroupId into lpBpJoin
                                from lpBpOpt in lpBpJoin.DefaultIfEmpty()
                                join lpBpAcc in _db.Accounts.AsNoTracking() on lpBpOpt.AccountId equals lpBpAcc.Id into lpBpAccJoin
                                from lpBpAccOpt in lpBpAccJoin.DefaultIfEmpty()
                                // contact fallback for main bank account
                                join cont in _db.Contacts.AsNoTracking() on bpAccOpt.BankContactId equals cont.Id into contJoin
                                from contOpt in contJoin.DefaultIfEmpty()
                                // contact fallback for linked posting's bank account
                                join lpCont in _db.Contacts.AsNoTracking() on lpBpAccOpt.BankContactId equals lpCont.Id into lpContJoin
                                from lpContOpt in lpContJoin.DefaultIfEmpty()
                                select new
                                {
                                    Id = x.P.Id,
                                    BookingDate = x.P.BookingDate,
                                    ValutaDate = x.P.ValutaDate,
                                    Amount = x.P.Amount,
                                    Kind = x.P.Kind,
                                    AccountId = x.P.AccountId,
                                    ContactId = x.P.ContactId,
                                    SavingsPlanId = x.P.SavingsPlanId,
                                    SecurityId = x.P.SecurityId,
                                    SourceId = x.P.SourceId,
                                    Subject = x.Subject,
                                    Recipient = x.P.RecipientName ?? x.Recipient,
                                    Description = x.P.Description ?? x.Description,
                                    SecuritySubType = x.P.SecuritySubType,
                                    Quantity = x.P.Quantity,
                                    GroupId = x.P.GroupId,
                                    LinkedPostingId = x.P.LinkedPostingId,
                                    LinkedPostingKind = lpOpt != null ? lpOpt.Kind : (PostingKind?)null,
                                    LinkedPostingAccountId = lpBpAccOpt != null ? lpBpAccOpt.Id : lpOpt != null ? lpOpt.AccountId : (Guid?)null,
                                    // raw symbol sources (do not compute fallback here)
                                    LinkedPostingAccountSymbolFromAccount = lpBpAccOpt != null ? lpBpAccOpt.SymbolAttachmentId : (Guid?)null,
                                    LinkedPostingAccountSymbolFromContact = lpContOpt != null ? lpContOpt.SymbolAttachmentId : (Guid?)null,
                                    LinkedPostingAccountName = lpBpAccOpt != null ? lpBpAccOpt.Name : null,
                                    BankPostingAccountId = bpOpt != null ? bpOpt.AccountId : (Guid?)null,
                                    BankPostingAccountSymbolFromAccount = bpAccOpt != null ? bpAccOpt.SymbolAttachmentId : (Guid?)null,
                                    BankPostingAccountSymbolFromContact = contOpt != null ? contOpt.SymbolAttachmentId : (Guid?)null,
                                    BankPostingAccountName = bpAccOpt != null ? bpAccOpt.Name : null
                                };

            var rows = await queryProjected.ToListAsync(ct);

            var result = rows.Select(r =>
            {
                // pick symbol fallback in-memory to avoid EF translation problems
                Guid? linkedSymbol = r.LinkedPostingAccountSymbolFromAccount ?? r.LinkedPostingAccountSymbolFromContact;
                Guid? bankSymbol = r.BankPostingAccountSymbolFromAccount ?? r.BankPostingAccountSymbolFromContact;

                return new PostingServiceDto(
                    r.Id,
                    r.BookingDate,
                    r.ValutaDate,
                    r.Amount,
                    r.Kind,
                    r.AccountId,
                    r.ContactId,
                    r.SavingsPlanId,
                    r.SecurityId,
                    r.SourceId,
                    r.Subject,
                    r.Recipient,
                    r.Description,
                    r.SecuritySubType != null ? (int?)r.SecuritySubType : null,
                    r.Quantity,
                    r.GroupId,
                    r.LinkedPostingId,
                    r.LinkedPostingKind != null ? (int?)r.LinkedPostingKind : null,
                    r.LinkedPostingAccountId,
                    linkedSymbol,
                    r.LinkedPostingAccountName,
                    r.BankPostingAccountId,
                    bankSymbol,
                    r.BankPostingAccountName);
            }).ToList();

            return result;
        }

        public async Task<IReadOnlyList<PostingServiceDto>> GetAccountPostingsAsync(Guid accountId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 250);
            bool owned = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.OwnerUserId == currentUserId, ct);
            if (!owned) return Array.Empty<PostingServiceDto>();

            var postings = _db.Postings.AsNoTracking().Where(p => p.AccountId == accountId && p.Kind == PostingKind.Bank);

            if (from.HasValue)
            {
                var f = from.Value.Date; postings = postings.Where(p => p.BookingDate >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); postings = postings.Where(p => p.BookingDate < t);
            }

            var joined = from p in postings
                         join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                         from seOpt in seJoin.DefaultIfEmpty()
                         join acc in _db.Accounts.AsNoTracking() on p.AccountId equals acc.Id into accJoin
                         from accOpt in accJoin.DefaultIfEmpty()
                         join cont in _db.Contacts.AsNoTracking() on accOpt.BankContactId equals cont.Id into contJoin
                         from contOpt in contJoin.DefaultIfEmpty()
                         select new
                         {
                             P = p,
                             Subject = p.Subject ?? seOpt.Subject,
                             Recipient = p.RecipientName ?? seOpt.RecipientName,
                             Description = p.Description ?? seOpt.BookingDescription,
                             AccountSymbolFromAccount = accOpt != null ? accOpt.SymbolAttachmentId : (Guid?)null,
                             AccountSymbolFromContact = contOpt != null ? contOpt.SymbolAttachmentId : (Guid?)null,
                             AccountName = accOpt != null ? accOpt.Name : null
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

            var ordered = joined.OrderByDescending(x => x.P.ValutaDate).ThenByDescending(x => x.P.BookingDate).ThenByDescending(x => x.P.Id).Skip(skip).Take(take);

            var rows = await ordered.ToListAsync(ct);

            var result = rows.Select(r =>
            {
                Guid? bankSymbol = r.AccountSymbolFromAccount ?? r.AccountSymbolFromContact;
                return new PostingServiceDto(
                    r.P.Id,
                    r.P.BookingDate,
                    r.P.ValutaDate,
                    r.P.Amount,
                    r.P.Kind,
                    r.P.AccountId,
                    r.P.ContactId,
                    r.P.SavingsPlanId,
                    r.P.SecurityId,
                    r.P.SourceId,
                    r.Subject,
                    r.Recipient,
                    r.Description,
                    r.P.SecuritySubType != null ? (int?)r.P.SecuritySubType : null,
                    r.P.Quantity,
                    r.P.GroupId,
                    (Guid?)null,
                    (int?)null,
                    (Guid?)null,
                    (Guid?)null,
                    (string?)null,
                    r.P.AccountId,
                    bankSymbol,
                    r.AccountName);
            }).ToList();

            return result;
        }

        public async Task<IReadOnlyList<PostingServiceDto>> GetSavingsPlanPostingsAsync(Guid planId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 250);
            bool owned = await _db.SavingsPlans.AsNoTracking().AnyAsync(s => s.Id == planId && s.OwnerUserId == currentUserId, ct);
            if (!owned) return Array.Empty<PostingServiceDto>();

            var query = _db.Postings.AsNoTracking().Where(p => p.SavingsPlanId == planId && p.Kind == PostingKind.SavingsPlan);

            if (from.HasValue)
            {
                var f = from.Value.Date; query = query.Where(p => p.BookingDate >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); query = query.Where(p => p.BookingDate < t);
            }

            var joined = from p in query
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

            var ordered = joined.OrderByDescending(x => x.P.ValutaDate).ThenByDescending(x => x.P.BookingDate).ThenByDescending(x => x.P.Id).Skip(skip).Take(take);

            var rows = await ordered.ToListAsync(ct);

            var result = rows.Select(r => new PostingServiceDto(
                r.P.Id,
                r.P.BookingDate,
                r.P.ValutaDate,
                r.P.Amount,
                r.P.Kind,
                r.P.AccountId,
                r.P.ContactId,
                r.P.SavingsPlanId,
                r.P.SecurityId,
                r.P.SourceId,
                r.Subject,
                r.Recipient,
                r.Description,
                r.P.SecuritySubType != null ? (int?)r.P.SecuritySubType : null,
                r.P.Quantity,
                r.P.GroupId,
                (Guid?)null,
                (int?)null,
                (Guid?)null,
                (Guid?)null,
                (string?)null,
                (Guid?)null,
                (Guid?)null,
                (string?)null)).ToList();

            return result;
        }

        public async Task<IReadOnlyList<PostingServiceDto>> GetSecurityPostingsAsync(Guid securityId, int skip, int take, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 250);
            bool owned = await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == securityId && s.OwnerUserId == currentUserId, ct);
            if (!owned) return Array.Empty<PostingServiceDto>();

            var query = _db.Postings.AsNoTracking().Where(p => p.SecurityId == securityId && p.Kind == PostingKind.Security);

            if (from.HasValue)
            {
                var f = from.Value.Date; query = query.Where(p => p.BookingDate >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); query = query.Where(p => p.BookingDate < t);
            }

            var joined = from p in query
                         join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                         from seOpt in seJoin.DefaultIfEmpty()
                         select new
                         {
                             P = p,
                             Subject = p.Subject ?? seOpt.Subject,
                             Recipient = p.RecipientName ?? seOpt.RecipientName,
                             Description = p.Description ?? seOpt.BookingDescription
                         };

            var ordered = joined.OrderByDescending(x => x.P.ValutaDate).ThenByDescending(x => x.P.BookingDate).ThenByDescending(x => x.P.Id).Skip(skip).Take(take);

            var rows = await ordered.ToListAsync(ct);

            var result = rows.Select(r => new PostingServiceDto(
                r.P.Id,
                r.P.BookingDate,
                r.P.ValutaDate,
                r.P.Amount,
                r.P.Kind,
                r.P.AccountId,
                r.P.ContactId,
                r.P.SavingsPlanId,
                r.P.SecurityId,
                r.P.SourceId,
                r.Subject,
                r.Recipient,
                r.Description,
                r.P.SecuritySubType != null ? (int?)r.P.SecuritySubType : null,
                r.P.Quantity,
                r.P.GroupId,
                (Guid?)null,
                (int?)null,
                (Guid?)null,
                (Guid?)null,
                (string?)null,
                (Guid?)null,
                (Guid?)null,
                (string?)null)).ToList();

            return result;
        }
    }
}
