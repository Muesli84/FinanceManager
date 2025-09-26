using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain; // PostingKind
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports; // ReportInterval
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class ReportAggregationServiceAdditionalTests
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

    private static Contact CreateContact(AppDbContext db, Guid userId, string name, ContactCategory? cat = null)
    {
        var c = new Contact(userId, name, FinanceManager.Shared.Dtos.ContactType.Person, cat?.Id, null);
        db.Contacts.Add(c);
        return c;
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnOnlyEntityRows_WhenIncludeCategoryFalse()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var cat = new ContactCategory(user.Id, "Food");
        db.ContactCategories.Add(cat);
        var c1 = CreateContact(db, user.Id, "A", cat);
        var c2 = CreateContact(db, user.Id, "B", cat);
        await db.SaveChangesAsync();

        var jan = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(2025,1,1), AggregatePeriod.Month); jan.Add(10);
        var feb = new PostingAggregate(PostingKind.Contact, null, c2.Id, null, null, new DateTime(2025,2,1), AggregatePeriod.Month); feb.Add(20);
        db.PostingAggregates.AddRange(jan,feb);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var result = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 12, IncludeCategory: false, ComparePrevious: true, CompareYear: false), CancellationToken.None);

        result.Points.Should().OnlyContain(p => p.GroupKey.StartsWith("Contact:"));
        result.Points.Should().NotContain(p => p.GroupKey.StartsWith("Category:"));
        // Previous for Feb contact (c2) should be null because it only has single period itself
        var febPoint = result.Points.Single(p => p.GroupKey == $"Contact:{c2.Id}" && p.PeriodStart == new DateTime(2025,2,1));
        febPoint.PreviousAmount.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_ShouldCreateZeroRowForMissingLatestPeriod_WhenComparisonsEnabled()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var c1 = CreateContact(db, user.Id, "A");
        var c2 = CreateContact(db, user.Id, "B");
        await db.SaveChangesAsync();

        // c1 has Jan & Feb, c2 only Jan. Latest period Feb. IncludeCategory true to also test parent/child creation.
        var c1Jan = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(2025,1,1), AggregatePeriod.Month); c1Jan.Add(10);
        var c1Feb = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(2025,2,1), AggregatePeriod.Month); c1Feb.Add(20);
        var c2Jan = new PostingAggregate(PostingKind.Contact, null, c2.Id, null, null, new DateTime(2025,1,1), AggregatePeriod.Month); c2Jan.Add(30);
        db.PostingAggregates.AddRange(c1Jan,c1Feb,c2Jan);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var result = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 12, IncludeCategory: true, ComparePrevious: true, CompareYear: false), CancellationToken.None);

        var latest = new DateTime(2025,2,1);
        // c2 should have auto-added zero row in Feb with previous = Jan amount
        var c2Feb = result.Points.SingleOrDefault(p => p.GroupKey == $"Contact:{c2.Id}" && p.PeriodStart == latest);
        c2Feb.Should().NotBeNull();
        c2Feb!.Amount.Should().Be(0m);
        c2Feb.PreviousAmount.Should().Be(30m);
    }

    [Fact]
    public async Task QueryAsync_ShouldRemoveEmptyGroupWithoutComparisonData()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var c1 = CreateContact(db, user.Id, "A"); // will have zero historic amount only
        var c2 = CreateContact(db, user.Id, "B");
        await db.SaveChangesAsync();

        var c1Jan = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(2025,1,1), AggregatePeriod.Month); // amount 0
        var c2Feb = new PostingAggregate(PostingKind.Contact, null, c2.Id, null, null, new DateTime(2025,2,1), AggregatePeriod.Month); c2Feb.Add(50);
        db.PostingAggregates.AddRange(c1Jan,c2Feb);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var result = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 12, IncludeCategory: false, ComparePrevious: true, CompareYear: true), CancellationToken.None);

        // Group for c1 should be removed (only zero data + zero row + no previous/year non-zero)
        result.Points.Should().NotContain(p => p.GroupKey == $"Contact:{c1.Id}");
        result.Points.Should().Contain(p => p.GroupKey == $"Contact:{c2.Id}");
    }

    [Fact]
    public async Task QueryAsync_ShouldRespectTake_PeriodLimitation()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var c1 = CreateContact(db, user.Id, "A");
        await db.SaveChangesAsync();

        // Create 15 consecutive months starting Jan 2024
        for (int i=0;i<15;i++)
        {
            var dt = new DateTime(2024,1,1).AddMonths(i);
            var agg = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(dt.Year, dt.Month,1), AggregatePeriod.Month);
            agg.Add(i+1);
            db.PostingAggregates.Add(agg);
        }
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var take = 5;
        var result = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, take, IncludeCategory: false, ComparePrevious: false, CompareYear: false), CancellationToken.None);

        var periods = result.Points.Select(p => p.PeriodStart).Distinct().OrderBy(d=>d).ToList();
        periods.Should().HaveCount(take);
        periods.First().Should().Be(new DateTime(2024,11,1)); // last 5 of 15 months (2024-11 .. 2025-03)
    }

    [Fact]
    public async Task QueryAsync_ShouldAggregateQuarterHalfYearYear()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var c = CreateContact(db, user.Id, "A");
        await db.SaveChangesAsync();

        // Q1/Q2 2024, H1/H2 2024, Years 2024/2025
        var q1 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2024,1,1), AggregatePeriod.Quarter); q1.Add(100);
        var q2 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2024,4,1), AggregatePeriod.Quarter); q2.Add(150);
        var h1 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2024,1,1), AggregatePeriod.HalfYear); h1.Add(250);
        var h2 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2024,7,1), AggregatePeriod.HalfYear); h2.Add(300);
        var y2024 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2024,1,1), AggregatePeriod.Year); y2024.Add(550);
        var y2025 = new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, new DateTime(2025,1,1), AggregatePeriod.Year); y2025.Add(50);
        db.PostingAggregates.AddRange(q1,q2,h1,h2,y2024,y2025);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);

        var quarters = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Quarter, 10, false, true, true), CancellationToken.None);
        quarters.Points.Where(p=>p.GroupKey.StartsWith("Contact:")).Should().HaveCount(2);
        var q2Point = quarters.Points.Single(p=>p.PeriodStart == new DateTime(2024,4,1) && p.GroupKey.StartsWith("Contact:"));
        q2Point.PreviousAmount.Should().Be(100);

        var halfYears = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.HalfYear, 10, false, true, false), CancellationToken.None);
        halfYears.Points.Should().HaveCount(2);
        var h2Point = halfYears.Points.Single(p=>p.PeriodStart == new DateTime(2024,7,1));
        h2Point.PreviousAmount.Should().Be(250);

        var years = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Year, 10, false, true, true), CancellationToken.None);
        years.Points.Should().HaveCount(2);
        var y2025Point = years.Points.Single(p=>p.PeriodStart == new DateTime(2025,1,1));
        y2025Point.PreviousAmount.Should().Be(550);
        y2025Point.YearAgoAmount.Should().Be(550);
    }

    [Fact]
    public async Task QueryAsync_ShouldGroupUncategorizedContacts()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user);
        var c1 = CreateContact(db, user.Id, "NoCat");
        await db.SaveChangesAsync();
        var agg = new PostingAggregate(PostingKind.Contact, null, c1.Id, null, null, new DateTime(2025,3,1), AggregatePeriod.Month); agg.Add(42);
        db.PostingAggregates.Add(agg);
        await db.SaveChangesAsync();
        var sut = new ReportAggregationService(db);
        var result = await sut.QueryAsync(new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 5, IncludeCategory: true, ComparePrevious: false, CompareYear: false), CancellationToken.None);
        result.Points.Should().Contain(p => p.GroupKey == $"Category:{PostingKind.Contact}:_none" && p.Amount == 42m);
        var child = result.Points.Single(p => p.GroupKey == $"Contact:{c1.Id}");
        child.ParentGroupKey.Should().Be($"Category:{PostingKind.Contact}:_none");
    }
}
