using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports;
using FinanceManager.Domain.Savings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class ReportAggregationServiceMultiKindTests
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

    private static Contact NewContact(AppDbContext db, Guid ownerId, string name, ContactCategory? cat = null)
    {
        var c = new Contact(ownerId, name, FinanceManager.Shared.Dtos.ContactType.Person, cat?.Id, null);
        db.Contacts.Add(c);
        return c;
    }

    private static SavingsPlan NewSavingsPlan(AppDbContext db, Guid ownerId, string name, Guid? categoryId = null)
    {
        var sp = new SavingsPlan(ownerId, name, FinanceManager.Shared.Dtos.SavingsPlanType.Recurring, null, null, null, categoryId);
        db.SavingsPlans.Add(sp);
        return sp;
    }

    [Fact]
    public async Task QueryAsync_MultiKinds_WithCategories_ShouldCreateTypeCategoryEntityHierarchy()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user);
        var contactCat = new ContactCategory(user.Id, "Friends");
        db.ContactCategories.Add(contactCat);
        await db.SaveChangesAsync();

        var c = NewContact(db, user.Id, "Alice", contactCat);
        var sp = NewSavingsPlan(db, user.Id, "ETF Plan");
        await db.SaveChangesAsync();

        var jan = new DateTime(2025,1,1);
        var feb = new DateTime(2025,2,1);
        db.PostingAggregates.AddRange(
            new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, jan, AggregatePeriod.Month).WithAdd(10).WithAdd(10),
            new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, feb, AggregatePeriod.Month).WithAdd(10).WithAdd(10),
            new PostingAggregate(PostingKind.SavingsPlan, null, null, sp.Id, null, jan, AggregatePeriod.Month).WithAdd(15).WithAdd(15),
            new PostingAggregate(PostingKind.SavingsPlan, null, null, sp.Id, null, feb, AggregatePeriod.Month).WithAdd(15).WithAdd(15)
        );
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var query = new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 12, IncludeCategory: true, ComparePrevious: false, CompareYear: false, PostingKinds: new []{ (int)PostingKind.Contact, (int)PostingKind.SavingsPlan });
        var result = await sut.QueryAsync(query, CancellationToken.None);

        // Expect type rows for both kinds in latest month (feb)
        var typeContact = result.Points.Single(p => p.GroupKey == $"Type:{PostingKind.Contact}" && p.PeriodStart == feb);
        var typeSavings = result.Points.Single(p => p.GroupKey == $"Type:{PostingKind.SavingsPlan}" && p.PeriodStart == feb);
        typeContact.Amount.Should().Be(20m); // category sum of contacts
        typeSavings.Amount.Should().Be(30m); // category sum of savings plans (uncategorized)

        // Category nodes exist with parent=Type
        var catContact = result.Points.Single(p => p.GroupKey == $"Category:{PostingKind.Contact}:{contactCat.Id}" && p.PeriodStart == feb);
        catContact.ParentGroupKey.Should().Be($"Type:{PostingKind.Contact}");
        var catSavings = result.Points.Single(p => p.GroupKey == $"Category:{PostingKind.SavingsPlan}:_none" && p.PeriodStart == feb);
        catSavings.ParentGroupKey.Should().Be($"Type:{PostingKind.SavingsPlan}");

        // Entity nodes exist with parent=Category when includeCategory=true in multi
        var contactEntity = result.Points.Single(p => p.GroupKey.StartsWith("Contact:") && p.PeriodStart == feb);
        contactEntity.ParentGroupKey.Should().Be(catContact.GroupKey);
        contactEntity.Amount.Should().Be(20m);
        var savingsEntity = result.Points.Single(p => p.GroupKey.StartsWith("SavingsPlan:") && p.PeriodStart == feb);
        savingsEntity.ParentGroupKey.Should().Be(catSavings.GroupKey);
        savingsEntity.Amount.Should().Be(30m);
    }

    [Fact]
    public async Task QueryAsync_MultiKinds_WithoutCategories_ShouldCreateTypeRowsWithEntityChildren()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var c = NewContact(db, user.Id, "Bob");
        var sp = NewSavingsPlan(db, user.Id, "Depot Plan");
        await db.SaveChangesAsync();

        var feb = new DateTime(2025,2,1);
        db.PostingAggregates.AddRange(
            new PostingAggregate(PostingKind.Contact, null, c.Id, null, null, feb, AggregatePeriod.Month).WithAdd(12).WithAdd(8), // 20
            new PostingAggregate(PostingKind.SavingsPlan, null, null, sp.Id, null, feb, AggregatePeriod.Month).WithAdd(5).WithAdd(7) // 12
        );
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var query = new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 6, IncludeCategory: false, ComparePrevious: false, CompareYear: false, PostingKinds: new []{ (int)PostingKind.Contact, (int)PostingKind.SavingsPlan });
        var result = await sut.QueryAsync(query, CancellationToken.None);

        // Type rows exist and sum entity amounts
        var typeContact = result.Points.Single(p => p.GroupKey == $"Type:{PostingKind.Contact}" && p.PeriodStart == feb);
        var typeSavings = result.Points.Single(p => p.GroupKey == $"Type:{PostingKind.SavingsPlan}" && p.PeriodStart == feb);
        typeContact.Amount.Should().Be(20m);
        typeSavings.Amount.Should().Be(12m);

        // No category nodes
        result.Points.Should().NotContain(p => p.GroupKey.StartsWith("Category:"));

        // Entities parent is Type
        var contactEntity = result.Points.Single(p => p.GroupKey.StartsWith("Contact:") && p.PeriodStart == feb);
        contactEntity.ParentGroupKey.Should().Be($"Type:{PostingKind.Contact}");
        var savingsEntity = result.Points.Single(p => p.GroupKey.StartsWith("SavingsPlan:") && p.PeriodStart == feb);
        savingsEntity.ParentGroupKey.Should().Be($"Type:{PostingKind.SavingsPlan}");
    }
}

internal static class PostingAggregateTestExtensions
{
    public static PostingAggregate WithAdd(this PostingAggregate agg, decimal amount)
    {
        agg.Add(amount);
        return agg;
    }
}
