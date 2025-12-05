using FinanceManager.Application.Aggregates;
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Aggregates;

public sealed class PostingAggregateService : IPostingAggregateService
{
    private readonly AppDbContext _db;

    public PostingAggregateService(AppDbContext db)
    {
        _db = db;
    }

    private static DateTime GetPeriodStart(DateTime date, AggregatePeriod p)
    {
        var d = date.Date;
        return p switch
        {
            AggregatePeriod.Month => new DateTime(d.Year, d.Month, 1),
            AggregatePeriod.Quarter => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
            AggregatePeriod.HalfYear => new DateTime(d.Year, (d.Month <= 6 ? 1 : 7), 1),
            AggregatePeriod.Year => new DateTime(d.Year, 1, 1),
            _ => new DateTime(d.Year, d.Month, 1)
        };
    }

    public async Task UpsertForPostingAsync(Posting posting, CancellationToken ct)
    {
        if (posting.Amount == 0m) { return; }
        var periods = new[] { AggregatePeriod.Month, AggregatePeriod.Quarter, AggregatePeriod.HalfYear, AggregatePeriod.Year };
        foreach (var p in periods)
        {
            var bookingStart = GetPeriodStart(posting.BookingDate, p);
            var valutaStart = GetPeriodStart(posting.ValutaDate, p);

            async Task Upsert(Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, SecurityPostingSubType? securitySubType, DateTime periodStart, AggregateDateKind dateKind)
            {
                var agg = _db.PostingAggregates.Local.FirstOrDefault(x => x.Kind == posting.Kind
                    && x.AccountId == accountId
                    && x.ContactId == contactId
                    && x.SavingsPlanId == savingsPlanId
                    && x.SecurityId == securityId
                    && x.SecuritySubType == securitySubType
                    && x.Period == p
                    && x.PeriodStart == periodStart
                    && x.DateKind == dateKind);

                if (agg == null)
                {
                    agg = await _db.PostingAggregates
                        .FirstOrDefaultAsync(x => x.Kind == posting.Kind
                            && x.AccountId == accountId
                            && x.ContactId == contactId
                            && x.SavingsPlanId == savingsPlanId
                            && x.SecurityId == securityId
                            && x.SecuritySubType == securitySubType
                            && x.Period == p
                            && x.PeriodStart == periodStart
                            && x.DateKind == dateKind, ct);
                }

                if (agg == null)
                {
                    agg = new PostingAggregate(posting.Kind, accountId, contactId, savingsPlanId, securityId, periodStart, p, securitySubType, dateKind);
                    _db.PostingAggregates.Add(agg);
                }
                agg.Add(posting.Amount);
            }

            switch (posting.Kind)
            {
                case PostingKind.Bank:
                    await Upsert(posting.AccountId, null, null, null, null, bookingStart, AggregateDateKind.Booking);
                    await Upsert(posting.AccountId, null, null, null, null, valutaStart, AggregateDateKind.Valuta);
                    break;
                case PostingKind.Contact:
                    await Upsert(null, posting.ContactId, null, null, null, bookingStart, AggregateDateKind.Booking);
                    await Upsert(null, posting.ContactId, null, null, null, valutaStart, AggregateDateKind.Valuta);
                    break;
                case PostingKind.SavingsPlan:
                    await Upsert(null, null, posting.SavingsPlanId, null, null, bookingStart, AggregateDateKind.Booking);
                    await Upsert(null, null, posting.SavingsPlanId, null, null, valutaStart, AggregateDateKind.Valuta);
                    break;
                case PostingKind.Security:
                    await Upsert(null, null, null, posting.SecurityId, posting.SecuritySubType, bookingStart, AggregateDateKind.Booking);
                    await Upsert(null, null, null, posting.SecurityId, posting.SecuritySubType, valutaStart, AggregateDateKind.Valuta);
                    break;
                default:
                    break;
            }
        }
    }

    public async Task RebuildForUserAsync(Guid userId, Action<int, int> progressCallback, CancellationToken ct)
    {
        // 1) User-Scope bestimmen
        var accountIds = await _db.Accounts.AsNoTracking().Where(a => a.OwnerUserId == userId).Select(a => a.Id).ToListAsync(ct);
        var contactIds = await _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == userId).Select(c => c.Id).ToListAsync(ct);
        var savingsPlanIds = await _db.SavingsPlans.AsNoTracking().Where(s => s.OwnerUserId == userId).Select(s => s.Id).ToListAsync(ct);
        var securityIds = await _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == userId).Select(s => s.Id).ToListAsync(ct);

        // 2) Bestehende Aggregatzeilen für diesen Scope set-basiert löschen (schnell)
        await _db.PostingAggregates
            .Where(p => (p.AccountId != null && accountIds.Contains(p.AccountId.Value))
                     || (p.ContactId != null && contactIds.Contains(p.ContactId.Value))
                     || (p.SavingsPlanId != null && savingsPlanIds.Contains(p.SavingsPlanId.Value))
                     || (p.SecurityId != null && securityIds.Contains(p.SecurityId.Value)))
            .ExecuteDeleteAsync(ct);

        // 3) Relevante Postings minimal laden (inkl. ValutaDate)
        var postings = await _db.Postings.AsNoTracking()
            .Where(p => (p.AccountId != null && accountIds.Contains(p.AccountId.Value))
                     || (p.ContactId != null && contactIds.Contains(p.ContactId.Value))
                     || (p.SavingsPlanId != null && savingsPlanIds.Contains(p.SavingsPlanId.Value))
                     || (p.SecurityId != null && securityIds.Contains(p.SecurityId.Value)))
            .Select(p => new { p.Kind, p.AccountId, p.ContactId, p.SavingsPlanId, p.SecurityId, p.BookingDate, p.ValutaDate, p.Amount, p.SecuritySubType })
            .ToListAsync(ct);

        // 4) In-Memory verdichten: Key = (Kind, DimensionIds, Period, PeriodStart, DateKind)
        var periods = new[] { AggregatePeriod.Month, AggregatePeriod.Quarter, AggregatePeriod.HalfYear, AggregatePeriod.Year };
        var sums = new Dictionary<(PostingKind kind, Guid? acc, Guid? con, Guid? sav, Guid? sec, SecurityPostingSubType? sub, AggregatePeriod period, DateTime start, AggregateDateKind dateKind), decimal>();

        foreach (var p in postings)
        {
            if (p.Amount == 0m) { continue; }

            foreach (var period in periods)
            {
                // booking
                var startBooking = GetPeriodStart(p.BookingDate, period);
                // valuta
                var startValuta = GetPeriodStart(p.ValutaDate, period);

                (Guid? acc, Guid? con, Guid? sav, Guid? sec, SecurityPostingSubType? sub) dim = p.Kind switch
                {
                    PostingKind.Bank => (p.AccountId, null, null, null, null),
                    PostingKind.Contact => (null, p.ContactId, null, null, null),
                    PostingKind.SavingsPlan => (null, null, p.SavingsPlanId, null, null),
                    PostingKind.Security => (null, null, null, p.SecurityId, p.SecuritySubType),
                    _ => (null, null, null, null, null)
                };

                var keyBooking = (p.Kind, dim.acc, dim.con, dim.sav, dim.sec, dim.sub, period, startBooking, AggregateDateKind.Booking);
                sums.TryGetValue(keyBooking, out var currB);
                sums[keyBooking] = currB + p.Amount;

                // always produce valuta aggregates as well
                var keyValuta = (p.Kind, dim.acc, dim.con, dim.sav, dim.sec, dim.sub, period, startValuta, AggregateDateKind.Valuta);
                sums.TryGetValue(keyValuta, out var currV);
                sums[keyValuta] = currV + p.Amount;
            }
        }

        // 5) Aggregatzeilen in Batches einfügen
        var total = sums.Count;
        var processed = 0;
        const int batchSize = 500;
        var batch = new List<PostingAggregate>(batchSize);

        foreach (var kvp in sums)
        {
            processed++;
            var (kind, acc, con, sav, sec, sub, period, start, dateKind) = kvp.Key;
            var amount = kvp.Value;
            if (amount == 0m) { continue; }

            var agg = new PostingAggregate(kind, acc, con, sav, sec, start, period, sub, dateKind);
            agg.Add(amount);
            batch.Add(agg);

            if (batch.Count >= batchSize)
            {
                _db.PostingAggregates.AddRange(batch);
                await _db.SaveChangesAsync(ct);
                batch.Clear();
                progressCallback(processed, total);
            }
        }

        if (batch.Count > 0)
        {
            _db.PostingAggregates.AddRange(batch);
            await _db.SaveChangesAsync(ct);
        }

        progressCallback(total, total);

        // 6) Kontosalden (CurrentBalance) aus Bank-Postings neu berechnen
        var bankSums = await _db.Postings.AsNoTracking()
            .Where(p => p.Kind == PostingKind.Bank && p.AccountId != null && accountIds.Contains(p.AccountId.Value))
            .GroupBy(p => p.AccountId!.Value)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var byAcc = bankSums.ToDictionary(x => x.AccountId, x => x.Balance);
        var ownedAccounts = await _db.Accounts.Where(a => a.OwnerUserId == userId).ToListAsync(ct);
        foreach (var acc in ownedAccounts)
        {
            var val = byAcc.TryGetValue(acc.Id, out var b) ? b : 0m;
            // set via domain method to keep ModifiedUtc in sync
            var delta = val - acc.CurrentBalance;
            if (delta != 0m)
            {
                acc.AdjustBalance(delta);
            }
        }
        await _db.SaveChangesAsync(ct);
    }
}
