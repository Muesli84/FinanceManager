using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Shared.Dtos;
using FinanceManager.Domain.Reports;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure.Aggregates;

namespace FinanceManager.Tests.Reports;

public sealed class ContactPostingsValutaDateTests
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

    [Fact]
    public async Task Query_WithUseValutaDate_ContactPostingsGroupedByValuta()
    {
        using var db = CreateDb();
        var ct = CancellationToken.None;

        // Arrange: owner and one contact
        var user = new FinanceManager.Domain.Users.User("owner", "pw", false);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var contact = new Contact(user.Id, "Alice", ContactType.Person, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        var year = DateTime.UtcNow.Year;

        // Two postings with same booking-month but different valuta months
        var booking1 = new DateTime(year, 1, 10);
        var valuta1 = new DateTime(year, 1, 31);

        var booking2 = new DateTime(year, 1, 11);
        var valuta2 = new DateTime(year, 2, 1);

        var p1 = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Contact,
            accountId: null,
            contactId: contact.Id,
            savingsPlanId: null,
            securityId: null,
            bookingDate: booking1,
            valutaDate: valuta1,
            amount: 100m,
            subject: "P1",
            recipientName: null,
            description: null,
            securitySubType: null,
            quantity: null);

        var p2 = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Contact,
            accountId: null,
            contactId: contact.Id,
            savingsPlanId: null,
            securityId: null,
            bookingDate: booking2,
            valutaDate: valuta2,
            amount: 200m,
            subject: "P2",
            recipientName: null,
            description: null,
            securitySubType: null,
            quantity: null);

        db.Postings.AddRange(p1, p2);

        // Ensure aggregates are created for these postings (Valuta and Booking)
        var aggSvc = new PostingAggregateService(db);
        await aggSvc.UpsertForPostingAsync(p1, ct);
        await aggSvc.UpsertForPostingAsync(p2, ct);

        await db.SaveChangesAsync(ct);

        var sut = new ReportAggregationService(db);

        // Act: monthly report using ValutaDate
        var analysis = new DateTime(year, 2, 1);
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Contact,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            UseValutaDate: true,
            Filters: new ReportAggregationFilters(ContactIds: new[] { contact.Id })
        );

        var result = await sut.QueryAsync(query, ct);

        // Assert: points should be grouped by Valuta month (Jan and Feb)
        result.Interval.Should().Be(ReportInterval.Month);
        var jan = new DateTime(year, 1, 1);
        var feb = new DateTime(year, 2, 1);

        var janRow = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == jan);
        janRow.Amount.Should().Be(100m);

        var febRow = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == feb);
        febRow.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task Query_WithUseValutaDate_ContactPostingsGroupedByPostingDate()
    {
        using var db = CreateDb();
        var ct = CancellationToken.None;

        // Arrange: owner and one contact
        var user = new FinanceManager.Domain.Users.User("owner", "pw", false);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var contact = new Contact(user.Id, "Alice", ContactType.Person, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        var year = DateTime.UtcNow.Year;

        // Two postings with same booking-month but different valuta months
        var booking1 = new DateTime(year, 1, 10);
        var valuta1 = new DateTime(year, 1, 31);

        var booking2 = new DateTime(year, 1, 11);
        var valuta2 = new DateTime(year, 2, 1);

        var p1 = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Contact,
            accountId: null,
            contactId: contact.Id,
            savingsPlanId: null,
            securityId: null,
            bookingDate: booking1,
            valutaDate: valuta1,
            amount: 100m,
            subject: "P1",
            recipientName: null,
            description: null,
            securitySubType: null,
            quantity: null);

        var p2 = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Contact,
            accountId: null,
            contactId: contact.Id,
            savingsPlanId: null,
            securityId: null,
            bookingDate: booking2,
            valutaDate: valuta2,
            amount: 200m,
            subject: "P2",
            recipientName: null,
            description: null,
            securitySubType: null,
            quantity: null);

        db.Postings.AddRange(p1, p2);
        await db.SaveChangesAsync(ct);

        // Create posting aggregate entries (monthly) so aggregation by PostingDate uses them
        var aggJan = new PostingAggregate(PostingKind.Contact, null, contact.Id, null, null, new DateTime(year, 1, 1), AggregatePeriod.Month);
        aggJan.Add(300m); // combined amount for January booking-period
        db.PostingAggregates.Add(aggJan);
        await db.SaveChangesAsync(ct);

        var sut = new ReportAggregationService(db);

        // Act: monthly report using BookingDate (UseValutaDate = false)
        var analysis = new DateTime(year, 2, 1);
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Contact,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            UseValutaDate: false,
            Filters: new ReportAggregationFilters(ContactIds: new[] { contact.Id })
        );

        var result = await sut.QueryAsync(query, ct);

        // Assert: points should be grouped by Booking month (Jan) with agg value and Feb zero
        result.Interval.Should().Be(ReportInterval.Month);
        var jan = new DateTime(year, 1, 1);
        var feb = new DateTime(year, 2, 1);

        var janRow = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == jan);
        janRow.Amount.Should().Be(300m);

        var febRow = result.Points.Single(p => p.GroupKey == $"Contact:{contact.Id}" && p.PeriodStart == feb);
        febRow.Amount.Should().Be(0m);
    }
}
