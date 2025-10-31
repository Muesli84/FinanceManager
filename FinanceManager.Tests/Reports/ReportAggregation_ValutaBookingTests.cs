using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Domain.Contacts;

namespace FinanceManager.Tests.Reports;

public sealed class ReportAggregation_ValutaBookingTests
{
    private static AppDbContext CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static readonly (DateTime Booking, DateTime Valuta, decimal Amount)[] Rows = new[]
    {
        (new DateTime(2025,9,18), new DateTime(2025,9,18), -1m),
        (new DateTime(2025,9,18), new DateTime(2025,9,18), -13.56m),
        (new DateTime(2025,9,10), new DateTime(2025,10,20), -3.59m),
        (new DateTime(2025,9,9),  new DateTime(2025,10,20), -4.8m),
        (new DateTime(2025,9,9),  new DateTime(2025,10,20), -3.47m),
        (new DateTime(2025,9,8),  new DateTime(2025,10,20), -0.99m),
        (new DateTime(2025,9,5),  new DateTime(2025,10,20), -11.2m),
        (new DateTime(2025,8,28), new DateTime(2025,10,20), -5.2m),
        (new DateTime(2025,8,27), new DateTime(2025,10,20), -0.4m),
        (new DateTime(2025,8,26), new DateTime(2025,10,20), -2.48m),
        (new DateTime(2025,8,26), new DateTime(2025,10,20), -1.99m),
        (new DateTime(2025,8,25), new DateTime(2025,10,20), -2.08m),
        (new DateTime(2025,8,25), new DateTime(2025,10,20), -10.08m),
        (new DateTime(2025,8,18), new DateTime(2025,8,18), -22.57m),
        (new DateTime(2025,6,18), new DateTime(2025,6,18), -17.72m),
        (new DateTime(2025,6,18), new DateTime(2025,6,18), -19.61m),
        (new DateTime(2024,9,18), new DateTime(2024,9,18), -15.98m),
        (new DateTime(2024,9,18), new DateTime(2024,9,18), -12.74m),
        (new DateTime(2024,9,18), new DateTime(2024,9,18), -28.25m),
        (new DateTime(2024,8,19), new DateTime(2024,8,19), -0.94m),
        (new DateTime(2024,8,19), new DateTime(2024,8,19), -16.46m),
        (new DateTime(2024,8,19), new DateTime(2024,8,19), -33.87m),
        (new DateTime(2023,12,19), new DateTime(2023,12,19), -18.47m),
        (new DateTime(2023,11,27), new DateTime(2023,11,27), -13.24m),
        (new DateTime(2023,9,5),  new DateTime(2023,9,5),  -18.54m),
        (new DateTime(2023,8,28), new DateTime(2023,8,28), -20.31m),
        (new DateTime(2023,8,25), new DateTime(2023,8,25), -3.9m),
        (new DateTime(2023,8,23), new DateTime(2023,8,23), -26.74m),
        (new DateTime(2023,7,25), new DateTime(2023,7,25), -64.14m),
        (new DateTime(2023,6,20), new DateTime(2023,6,20), -1.34m),
        (new DateTime(2023,4,4),  new DateTime(2023,4,4),  -44.51m),
        (new DateTime(2022,8,29), new DateTime(2022,8,29), -19.63m),
        (new DateTime(2021,12,21),new DateTime(2021,12,21), -31.88m),
        (new DateTime(2021,12,7), new DateTime(2021,12,7),  -31.22m),
        (new DateTime(2021,9,23), new DateTime(2021,9,23), -15.43m),
        (new DateTime(2021,5,25), new DateTime(2021,5,25), -16.1m),
        (new DateTime(2021,3,30), new DateTime(2021,3,30), -0.94m),
        (new DateTime(2020,10,20),new DateTime(2020,10,20), -16.97m),
        (new DateTime(2020,9,15), new DateTime(2020,9,15), -12.51m),
        (new DateTime(2020,9,8),  new DateTime(2020,9,8),  -20.51m),
        (new DateTime(2020,1,28), new DateTime(2020,1,28), -13.65m),
        (new DateTime(2019,5,21), new DateTime(2019,5,21), -23.15m),
        (new DateTime(2019,2,5),  new DateTime(2019,2,5),  -31.54m),
        (new DateTime(2018,11,6), new DateTime(2018,11,6), -15.86m)
    };

    [Fact]
    public async Task QueryAsync_Monthly_ByValutaDate_ShouldAggregateSep2025()
    {
        using var db = CreateDb();
        var svc = new ReportAggregationService(db);
        var aggSvc = new PostingAggregateService(db);
        var ct = CancellationToken.None;

        // owner & contact
        var owner = new FinanceManager.Domain.Users.User("owner", "pw", false);
        db.Users.Add(owner);
        await db.SaveChangesAsync(ct);
        var contact = new Contact(owner.Id, "C1", ContactType.Person, null, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        // insert postings with Contact kind and upsert aggregates for each posting
        foreach (var r in Rows)
        {
            var p = new FinanceManager.Domain.Postings.Posting(
                Guid.NewGuid(), PostingKind.Contact,
                accountId: null, contactId: contact.Id, savingsPlanId: null, securityId: null,
                bookingDate: r.Booking, amount: r.Amount,
                subject: null, recipientName: null, description: null,
                securitySubType: null, quantity: null);
            // override ValutaDate to provided value
            p.SetValutaDate(r.Valuta);
            db.Postings.Add(p);
            // ensure aggregates are created (both Booking and Valuta) as in normal flow
            await aggSvc.UpsertForPostingAsync(p, ct);
        }
        await db.SaveChangesAsync(ct);

        var analysis = new DateTime(2025,9,1);
        var query = new ReportAggregationQuery(
            OwnerUserId: owner.Id,
            PostingKind: (int)PostingKind.Contact,
            Interval: ReportInterval.Month,
            Take: 24,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            UseValutaDate: true,
            Filters: new ReportAggregationFilters(ContactIds: new[] { contact.Id })
        );

        var result = await svc.QueryAsync(query, ct);
        result.Interval.Should().Be(ReportInterval.Month);
        var periodStart = new DateTime(2025,9,1);
        var row = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == periodStart);
        // Expect only two rows with valuta in Sep 2025: -1 and -13.56 => sum -14.56
        row.Amount.Should().BeApproximately(-14.56m, 0.0001m);

        // additionally assert that posting aggregates exist for booking AND valuta date kinds for relevant months
        var bookingAggExists = await db.PostingAggregates.AnyAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.DateKind == AggregateDateKind.Booking, ct);
        var valutaAggExists = await db.PostingAggregates.AnyAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.DateKind == AggregateDateKind.Valuta, ct);
        bookingAggExists.Should().BeTrue();
        valutaAggExists.Should().BeTrue();

        // Verify the aggregate value for Valuta Sep 2025 matches expected sum
        var expectedValutaSep = Rows.Where(r => r.Valuta.Year == 2025 && r.Valuta.Month == 9).Sum(r => r.Amount);
        var valutaAgg = await db.PostingAggregates.SingleAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.PeriodStart == periodStart && a.DateKind == AggregateDateKind.Valuta, ct);
        valutaAgg.Amount.Should().BeApproximately(expectedValutaSep, 0.0001m);

        // Ensure QueryAsync did not include periods beyond analysis month (e.g., Oct 2025)
        result.Points.Should().NotContain(p => p.PeriodStart > periodStart);
    }

    [Fact]
    public async Task QueryAsync_Monthly_ByBookingDate_ShouldAggregateSep2025()
    {
        using var db = CreateDb();
        var svc = new ReportAggregationService(db);
        var aggSvc = new PostingAggregateService(db);
        var ct = CancellationToken.None;

        // owner & contact
        var owner = new FinanceManager.Domain.Users.User("owner2", "pw", false);
        db.Users.Add(owner);
        await db.SaveChangesAsync(ct);
        var contact = new Contact(owner.Id, "C2", ContactType.Person, null, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        // insert postings (booking dates) and upsert aggregates for each posting
        foreach (var r in Rows)
        {
            var p = new FinanceManager.Domain.Postings.Posting(
                Guid.NewGuid(), PostingKind.Contact,
                accountId: null, contactId: contact.Id, savingsPlanId: null, securityId: null,
                bookingDate: r.Booking, amount: r.Amount,
                subject: null, recipientName: null, description: null,
                securitySubType: null, quantity: null);
            p.SetValutaDate(r.Valuta);
            db.Postings.Add(p);
            await aggSvc.UpsertForPostingAsync(p, ct);
        }
        await db.SaveChangesAsync(ct);

        var analysis = new DateTime(2025,9,1);
        var query = new ReportAggregationQuery(
            OwnerUserId: owner.Id,
            PostingKind: (int)PostingKind.Contact,
            Interval: ReportInterval.Month,
            Take: 24,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            UseValutaDate: false,
            Filters: new ReportAggregationFilters(ContactIds: new[] { contact.Id })
        );

        var result = await svc.QueryAsync(query, ct);
        result.Interval.Should().Be(ReportInterval.Month);
        var periodStart = new DateTime(2025,9,1);
        var row = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == periodStart);
        // Expect sum of bookings in Sep 2025: computed manually = -38.61
        row.Amount.Should().BeApproximately(-38.61m, 0.0001m);

        // ensure aggregates were created for both date kinds
        var bookingAggExists = await db.PostingAggregates.AnyAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.DateKind == AggregateDateKind.Booking, ct);
        var valutaAggExists = await db.PostingAggregates.AnyAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.DateKind == AggregateDateKind.Valuta, ct);
        bookingAggExists.Should().BeTrue();
        valutaAggExists.Should().BeTrue();

        // Verify the aggregate value for Booking Sep 2025 matches expected sum
        var expectedBookingSep = Rows.Where(r => r.Booking.Year == 2025 && r.Booking.Month == 9).Sum(r => r.Amount);
        var bookingAgg = await db.PostingAggregates.SingleAsync(a => a.Kind == PostingKind.Contact && a.ContactId == contact.Id && a.Period == AggregatePeriod.Month && a.PeriodStart == periodStart && a.DateKind == AggregateDateKind.Booking, ct);
        bookingAgg.Amount.Should().BeApproximately(expectedBookingSep, 0.0001m);

        // Ensure QueryAsync did not include periods beyond analysis month (e.g., Oct 2025)
        result.Points.Should().NotContain(p => p.PeriodStart > periodStart);
    }
}
