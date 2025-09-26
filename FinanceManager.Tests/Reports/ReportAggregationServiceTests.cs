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
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class ReportAggregationServiceTests
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
    public async Task QueryAsync_ShouldAggregateCategoriesAndComparisons_ForContactsAcrossMonths()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Drei Kategorien: A (1 Kontakt), B (2 Kontakte), C (3 Kontakte)
        var catA = new ContactCategory(user.Id, "Cat A");
        var catB = new ContactCategory(user.Id, "Cat B");
        var catC = new ContactCategory(user.Id, "Cat C");
        db.ContactCategories.AddRange(catA,catB,catC);
        await db.SaveChangesAsync();

        // Helper zum Erstellen eines Kontakts mit Basisbetrag (pro Posting) 10 / 20 / 30
        Contact NewContact(Guid ownerId, ContactCategory cat, string name) => new(ownerId, name, FinanceManager.Shared.Dtos.ContactType.Person, cat.Id, null);

        var cA1 = NewContact(user.Id, catA, "A1");

        var cB1 = NewContact(user.Id, catB, "B1");
        var cB2 = NewContact(user.Id, catB, "B2");

        var cC1 = NewContact(user.Id, catC, "C1");
        var cC2 = NewContact(user.Id, catC, "C2");
        var cC3 = NewContact(user.Id, catC, "C3");

        db.Contacts.AddRange(cA1,cB1,cB2,cC1,cC2,cC3);
        await db.SaveChangesAsync();

        // Monate Jan 2023 .. Sep 2025 (einschlieﬂlich)
        var start = new DateTime(2023,1,1);
        var end = new DateTime(2025,9,1);
        var months = new List<DateTime>();
        for(var dt = start; dt <= end; dt = dt.AddMonths(1)) { months.Add(new DateTime(dt.Year, dt.Month,1)); }

        // Pro Monat pro Kontakt zwei Postings: Kontakt 1 Basis 10Ä, Kontakt 2 Basis 20Ä, Kontakt 3 Basis 30Ä => Monatssumme = Basis * 2
        void AddMonthlyAggregates(Contact contact, decimal perPosting)
        {
            foreach(var m in months)
            {
                var agg = new PostingAggregate(PostingKind.Contact, null, contact.Id, null, null, m, AggregatePeriod.Month);
                agg.Add(perPosting); // erster Posten
                agg.Add(perPosting); // zweiter Posten
                db.PostingAggregates.Add(agg);
            }
        }

        AddMonthlyAggregates(cA1, 10m);

        AddMonthlyAggregates(cB1, 10m);
        AddMonthlyAggregates(cB2, 20m);

        AddMonthlyAggregates(cC1, 10m);
        AddMonthlyAggregates(cC2, 20m);
        AddMonthlyAggregates(cC3, 30m);

        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        // Take groﬂ genug, um alle 33 Monate abzudecken
        var query = new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Month, 40, IncludeCategory: true, ComparePrevious: true, CompareYear: true);
        var result = await sut.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.Points.Should().NotBeEmpty();
        result.Interval.Should().Be(ReportInterval.Month);
        result.ComparedPrevious.Should().BeTrue();
        result.ComparedYear.Should().BeTrue();

        // Kategorie-Gruppen-Keys
        string CatKey(ContactCategory cat) => $"Category:{PostingKind.Contact}:{cat.Id}";

        // Pr¸fe f¸r letzten Monat (Sep 2025)
        var lastPeriod = months.Last();
        var catAPoint = result.Points.Single(p => p.GroupKey == CatKey(catA) && p.PeriodStart == lastPeriod);
        var catBPoint = result.Points.Single(p => p.GroupKey == CatKey(catB) && p.PeriodStart == lastPeriod);
        var catCPoint = result.Points.Single(p => p.GroupKey == CatKey(catC) && p.PeriodStart == lastPeriod);

        // Erwartete Monatssummen (Basis 10/20/30 jeweils *2 Postings * Anzahl Kontakte je Kategorie)
        catAPoint.Amount.Should().Be(20m);              // 1 * (10*2)
        catBPoint.Amount.Should().Be(20m + 40m);        // (10*2) + (20*2) = 60
        catCPoint.Amount.Should().Be(20m + 40m + 60m);  // 20 + 40 + 60 = 120

        // Previous (Aug 2025) sollten identisch sein, da Betr‰ge konstant
        catAPoint.PreviousAmount.Should().Be(20m);
        catBPoint.PreviousAmount.Should().Be(60m);
        catCPoint.PreviousAmount.Should().Be(120m);

        // YearAgo (Sep 2024) ebenfalls identisch
        catAPoint.YearAgoAmount.Should().Be(20m);
        catBPoint.YearAgoAmount.Should().Be(60m);
        catCPoint.YearAgoAmount.Should().Be(120m);

        // Child-Entity Punkte: Pr¸fe, dass ParentGroupKey gesetzt ist und monatliche Summen korrekt (z.B. C3)
        var c3Point = result.Points.Single(p => p.GroupKey == $"Contact:{cC3.Id}" && p.PeriodStart == lastPeriod);
        c3Point.Amount.Should().Be(60m); // 30 * 2
        c3Point.ParentGroupKey.Should().Be(CatKey(catC));
        c3Point.PreviousAmount.Should().Be(60m);
        c3Point.YearAgoAmount.Should().Be(60m);

        // Fr¸hester Monat (Jan 2023) darf keine Previous/Year Werte haben
        var firstCatA = result.Points.Single(p => p.GroupKey == CatKey(catA) && p.PeriodStart == months.First());
        firstCatA.PreviousAmount.Should().BeNull();
        firstCatA.YearAgoAmount.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_ShouldAggregateYtdCategoriesAndComparisons_ForContacts()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var catA = new ContactCategory(user.Id, "Cat A");
        var catB = new ContactCategory(user.Id, "Cat B");
        var catC = new ContactCategory(user.Id, "Cat C");
        db.ContactCategories.AddRange(catA,catB,catC);
        await db.SaveChangesAsync();

        Contact NewContact(Guid ownerId, ContactCategory cat, string name) => new(ownerId, name, FinanceManager.Shared.Dtos.ContactType.Person, cat.Id, null);
        var cA1 = NewContact(user.Id, catA, "A1");
        var cB1 = NewContact(user.Id, catB, "B1"); var cB2 = NewContact(user.Id, catB, "B2");
        var cC1 = NewContact(user.Id, catC, "C1"); var cC2 = NewContact(user.Id, catC, "C2"); var cC3 = NewContact(user.Id, catC, "C3");
        db.Contacts.AddRange(cA1,cB1,cB2,cC1,cC2,cC3);
        await db.SaveChangesAsync();

        var start = new DateTime(2023,1,1); var end = new DateTime(2025,9,1);
        var months = new List<DateTime>();
        for(var dt = start; dt <= end; dt = dt.AddMonths(1)) { months.Add(new DateTime(dt.Year, dt.Month,1)); }

        void AddMonthlyAggregates(Contact contact, decimal perPosting)
        {
            foreach(var m in months)
            {
                var agg = new PostingAggregate(PostingKind.Contact, null, contact.Id, null, null, m, AggregatePeriod.Month);
                agg.Add(perPosting); agg.Add(perPosting);
                db.PostingAggregates.Add(agg);
            }
        }
        AddMonthlyAggregates(cA1, 10m); // cat A: 20/Monat
        AddMonthlyAggregates(cB1, 10m); AddMonthlyAggregates(cB2, 20m); // cat B: 60/Monat
        AddMonthlyAggregates(cC1, 10m); AddMonthlyAggregates(cC2, 20m); AddMonthlyAggregates(cC3, 30m); // cat C: 120/Monat
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var query = new ReportAggregationQuery(user.Id, (int)PostingKind.Contact, ReportInterval.Ytd, 40, IncludeCategory: true, ComparePrevious: true, CompareYear: true);
        var result = await sut.QueryAsync(query, CancellationToken.None);

        result.Interval.Should().Be(ReportInterval.Ytd);
        result.Points.Should().NotBeEmpty();

        string CatKey(ContactCategory cat) => $"Category:{PostingKind.Contact}:{cat.Id}";

        // Erwartete YTD Summen pro Jahr (latestMonth=12 wegen vorhandener Dezember 2023/2024, 2025 hat nur 9 Monate)
        decimal catA_2023 = 20m * 12; decimal catA_2024 = 20m * 12; decimal catA_2025 = 20m * 9;
        decimal catB_2023 = 60m * 12; decimal catB_2024 = 60m * 12; decimal catB_2025 = 60m * 9;
        decimal catC_2023 = 120m * 12; decimal catC_2024 = 120m * 12; decimal catC_2025 = 120m * 9;

        DateTime Y(int year) => new DateTime(year,1,1);

        var a2023 = result.Points.Single(p => p.GroupKey == CatKey(catA) && p.PeriodStart == Y(2023));
        var a2024 = result.Points.Single(p => p.GroupKey == CatKey(catA) && p.PeriodStart == Y(2024));
        var a2025 = result.Points.Single(p => p.GroupKey == CatKey(catA) && p.PeriodStart == Y(2025));
        a2023.Amount.Should().Be(catA_2023); a2024.Amount.Should().Be(catA_2024); a2025.Amount.Should().Be(catA_2025);

        var b2023 = result.Points.Single(p => p.GroupKey == CatKey(catB) && p.PeriodStart == Y(2023));
        var b2024 = result.Points.Single(p => p.GroupKey == CatKey(catB) && p.PeriodStart == Y(2024));
        var b2025 = result.Points.Single(p => p.GroupKey == CatKey(catB) && p.PeriodStart == Y(2025));
        b2023.Amount.Should().Be(catB_2023); b2024.Amount.Should().Be(catB_2024); b2025.Amount.Should().Be(catB_2025);

        var c2023 = result.Points.Single(p => p.GroupKey == CatKey(catC) && p.PeriodStart == Y(2023));
        var c2024 = result.Points.Single(p => p.GroupKey == CatKey(catC) && p.PeriodStart == Y(2024));
        var c2025 = result.Points.Single(p => p.GroupKey == CatKey(catC) && p.PeriodStart == Y(2025));
        c2023.Amount.Should().Be(catC_2023); c2024.Amount.Should().Be(catC_2024); c2025.Amount.Should().Be(catC_2025);

        // Previous / YearAgo: 2023 keine Werte, 2024 und 2025 jeweils Vorjahr
        a2023.PreviousAmount.Should().BeNull(); a2023.YearAgoAmount.Should().BeNull();
        a2024.PreviousAmount.Should().Be(catA_2023); a2024.YearAgoAmount.Should().Be(catA_2023);
        a2025.PreviousAmount.Should().Be(catA_2024); a2025.YearAgoAmount.Should().Be(catA_2024);

        b2024.PreviousAmount.Should().Be(catB_2023); b2024.YearAgoAmount.Should().Be(catB_2023);
        b2025.PreviousAmount.Should().Be(catB_2024); b2025.YearAgoAmount.Should().Be(catB_2024);

        c2024.PreviousAmount.Should().Be(catC_2023); c2024.YearAgoAmount.Should().Be(catC_2023);
        c2025.PreviousAmount.Should().Be(catC_2024); c2025.YearAgoAmount.Should().Be(catC_2024);

        // Child (z.B. C3)
        var c3_2025 = result.Points.Single(p => p.GroupKey == $"Contact:{cC3.Id}" && p.PeriodStart == Y(2025));
        c3_2025.Amount.Should().Be(60m * 9); // 30 *2 *9
        c3_2025.PreviousAmount.Should().Be(60m * 12); // 2024
        c3_2025.YearAgoAmount.Should().Be(60m * 12); // 2024
        c3_2025.ParentGroupKey.Should().Be(CatKey(catC));
    }
}
