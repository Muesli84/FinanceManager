using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain; // PostingKind, AccountType
using FinanceManager.Domain.Accounts; // Account
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Savings; // SavingsPlan
using FinanceManager.Domain.Securities; // Security
using FinanceManager.Shared.Dtos; // SavingsPlanType
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;

namespace FinanceManager.Tests.Reports;

public sealed class PostingTimeSeriesServiceTests
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
    public async Task GetAsync_ReturnsNull_WhenNotOwned()
    {
        using var db = CreateDb();
        var userA = new FinanceManager.Domain.Users.User("u1","pw",false);
        var userB = new FinanceManager.Domain.Users.User("u2","pw",false);
        db.Users.AddRange(userA,userB);
        var acc = new Account(userB.Id, AccountType.Giro, "Fremd", null, Guid.NewGuid());
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        var svc = new PostingTimeSeriesService(db);
        var res = await svc.GetAsync(userA.Id, PostingKind.Bank, acc.Id, AggregatePeriod.Month, 12, null, CancellationToken.None);
        res.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsOrderedAscending()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u1","pw",false);
        db.Users.Add(user);
        var bankContact = new FinanceManager.Domain.Contacts.Contact(user.Id, "Bank", FinanceManager.Shared.Dtos.ContactType.Bank, null, null);
        db.Contacts.Add(bankContact);
        var acc = new Account(user.Id, AccountType.Giro, "Konto", null, bankContact.Id);
        db.Accounts.Add(acc);
        var a2 = new PostingAggregate(PostingKind.Bank, acc.Id, null, null, null, new DateTime(2024,2,1), AggregatePeriod.Month);
        a2.Add(50m);
        var a1 = new PostingAggregate(PostingKind.Bank, acc.Id, null, null, null, new DateTime(2024,1,1), AggregatePeriod.Month);
        a1.Add(20m);
        db.PostingAggregates.AddRange(a2,a1);
        await db.SaveChangesAsync();
        var svc = new PostingTimeSeriesService(db);
        var res = await svc.GetAsync(user.Id, PostingKind.Bank, acc.Id, AggregatePeriod.Month, 10, null, CancellationToken.None);
        res!.Select(r=>r.PeriodStart).Should().ContainInOrder(new DateTime(2024,1,1), new DateTime(2024,2,1));
    }

    [Fact]
    public async Task GetAsync_RespectsTake_Defaults()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u1","pw",false);
        db.Users.Add(user);
        var bankContact = new FinanceManager.Domain.Contacts.Contact(user.Id, "Bank", FinanceManager.Shared.Dtos.ContactType.Bank, null, null);
        db.Contacts.Add(bankContact);
        var acc = new Account(user.Id, AccountType.Giro, "Konto", null, bankContact.Id);
        db.Accounts.Add(acc);
        for(int m=0;m<40;m++)
        {
            var dt = new DateTime(2021,1,1).AddMonths(m);
            var agg = new PostingAggregate(PostingKind.Bank, acc.Id, null, null, null, new DateTime(dt.Year, dt.Month,1), AggregatePeriod.Month);
            agg.Add(m+1);
            db.PostingAggregates.Add(agg);
        }
        await db.SaveChangesAsync();
        var svc = new PostingTimeSeriesService(db);
        var res = await svc.GetAsync(user.Id, PostingKind.Bank, acc.Id, AggregatePeriod.Month, 12, null, CancellationToken.None);
        res!.Count.Should().Be(12);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnOnlyAggregatesOfRequestedKindAndEntity()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        // Contacts
        var bankContact = new FinanceManager.Domain.Contacts.Contact(user.Id, "Bank", FinanceManager.Shared.Dtos.ContactType.Bank, null, null);
        var personA = new FinanceManager.Domain.Contacts.Contact(user.Id, "Alice", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        var personB = new FinanceManager.Domain.Contacts.Contact(user.Id, "Bob", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        db.Contacts.AddRange(bankContact, personA, personB);
        // Account + second account
        var acc1 = new Account(user.Id, AccountType.Giro, "Account1", null, bankContact.Id);
        var acc2 = new Account(user.Id, AccountType.Giro, "Account2", null, bankContact.Id);
        db.Accounts.AddRange(acc1, acc2);
        // Savings plans
        var plan1 = new SavingsPlan(user.Id, "PlanA", SavingsPlanType.OneTime, 1000m, DateTime.Today.AddMonths(6), null);
        var plan2 = new SavingsPlan(user.Id, "PlanB", SavingsPlanType.OneTime, 500m, DateTime.Today.AddMonths(3), null);
        db.SavingsPlans.AddRange(plan1, plan2);
        // Securities
        var sec1 = new Security(user.Id, "SecA", "IDA", null, null, "EUR", null);
        var sec2 = new Security(user.Id, "SecB", "IDB", null, null, "EUR", null);
        db.Securities.AddRange(sec1, sec2);
        await db.SaveChangesAsync();

        // Helper to create two months + one noise aggregate for each kind/entity
        void AddAgg(PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, decimal baseAmount)
        {
            var aJan = new PostingAggregate(kind, accountId, contactId, savingsPlanId, securityId, new DateTime(2024,1,1), AggregatePeriod.Month); aJan.Add(baseAmount);
            var aFeb = new PostingAggregate(kind, accountId, contactId, savingsPlanId, securityId, new DateTime(2024,2,1), AggregatePeriod.Month); aFeb.Add(baseAmount + 10);
            // noise (different entity id)
            Guid? nAcc = accountId.HasValue ? (accountId == acc1.Id ? acc2.Id : acc1.Id) : null;
            Guid? nContact = contactId.HasValue ? (contactId == personA.Id ? personB.Id : personA.Id) : null;
            Guid? nPlan = savingsPlanId.HasValue ? (savingsPlanId == plan1.Id ? plan2.Id : plan1.Id) : null;
            Guid? nSec = securityId.HasValue ? (securityId == sec1.Id ? sec2.Id : sec1.Id) : null;
            var noise = new PostingAggregate(kind, nAcc, nContact, nPlan, nSec, new DateTime(2024,1,1), AggregatePeriod.Month); noise.Add(999m);
            db.PostingAggregates.AddRange(aJan,aFeb,noise);
        }

        AddAgg(PostingKind.Bank, acc1.Id, null, null, null, 100m);
        AddAgg(PostingKind.Contact, null, personA.Id, null, null, 200m);
        AddAgg(PostingKind.SavingsPlan, null, null, plan1.Id, null, 300m);
        AddAgg(PostingKind.Security, null, null, null, sec1.Id, 400m);
        await db.SaveChangesAsync();

        var svc = new PostingTimeSeriesService(db);
        var bankSeries = await svc.GetAsync(user.Id, PostingKind.Bank, acc1.Id, AggregatePeriod.Month, 10, null, CancellationToken.None);
        var contactSeries = await svc.GetAsync(user.Id, PostingKind.Contact, personA.Id, AggregatePeriod.Month, 10, null, CancellationToken.None);
        var planSeries = await svc.GetAsync(user.Id, PostingKind.SavingsPlan, plan1.Id, AggregatePeriod.Month, 10, null, CancellationToken.None);
        var securitySeries = await svc.GetAsync(user.Id, PostingKind.Security, sec1.Id, AggregatePeriod.Month, 10, null, CancellationToken.None);

        bankSeries!.Should().HaveCount(2).And.OnlyContain(p => p.Amount < 999m);
        contactSeries!.Should().HaveCount(2).And.OnlyContain(p => p.Amount < 999m);
        planSeries!.Should().HaveCount(2).And.OnlyContain(p => p.Amount < 999m);
        securitySeries!.Should().HaveCount(2).And.OnlyContain(p => p.Amount < 999m);

        bankSeries.Select(p => p.Amount).Sum().Should().Be(100m + 110m);
        contactSeries.Select(p => p.Amount).Sum().Should().Be(200m + 210m);
        planSeries.Select(p => p.Amount).Sum().Should().Be(300m + 310m);
        securitySeries.Select(p => p.Amount).Sum().Should().Be(400m + 410m);
    }
}
