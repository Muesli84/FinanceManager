using FinanceManager.Domain.Postings;
using FinanceManager.Domain;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService
{
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

    private async Task UpsertAggregatesAsync(Domain.Postings.Posting posting, CancellationToken ct)
    {
        if (posting.Amount == 0m) { return; }
        var periods = new[] { AggregatePeriod.Month, AggregatePeriod.Quarter, AggregatePeriod.HalfYear, AggregatePeriod.Year };
        foreach (var p in periods)
        {
            var periodStart = GetPeriodStart(posting.BookingDate, p);

            async Task Upsert(Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId)
            {
                // Check ChangeTracker first to avoid duplicate creations within the same context
                var agg = _db.PostingAggregates.Local.FirstOrDefault(x => x.Kind == posting.Kind
                    && x.AccountId == accountId
                    && x.ContactId == contactId
                    && x.SavingsPlanId == savingsPlanId
                    && x.SecurityId == securityId
                    && x.Period == p
                    && x.PeriodStart == periodStart);

                if (agg == null)
                {
                    agg = await _db.PostingAggregates
                        .FirstOrDefaultAsync(x => x.Kind == posting.Kind
                            && x.AccountId == accountId
                            && x.ContactId == contactId
                            && x.SavingsPlanId == savingsPlanId
                            && x.SecurityId == securityId
                            && x.Period == p
                            && x.PeriodStart == periodStart, ct);
                }

                if (agg == null)
                {
                    agg = new Domain.Postings.PostingAggregate(posting.Kind, accountId, contactId, savingsPlanId, securityId, periodStart, p);
                    _db.PostingAggregates.Add(agg);
                }
                agg.Add(posting.Amount);
            }

            switch (posting.Kind)
            {
                case PostingKind.Bank:
                    await Upsert(posting.AccountId, null, null, null);
                    break;
                case PostingKind.Contact:
                    await Upsert(null, posting.ContactId, null, null);
                    break;
                case PostingKind.SavingsPlan:
                    await Upsert(null, null, posting.SavingsPlanId, null);
                    break;
                case PostingKind.Security:
                    await Upsert(null, null, null, posting.SecurityId);
                    break;
                default:
                    break;
            }
        }
    }
}
