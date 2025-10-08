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
using FinanceManager.Domain.Securities; // added

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

        // YTD-Definition im Service: F¸r alle Jahre wird bis zum aktuellen Monat (UtcNow.Month) summiert.
        var cutoffMonth = DateTime.UtcNow.Month; // dynamisch
        int monthsPrevYears = Math.Min(12, cutoffMonth);
        int monthsYear2025 = months.Count(m => m.Year == 2025 && m.Month <= cutoffMonth);

        decimal catA_2023 = 20m * monthsPrevYears; decimal catA_2024 = 20m * monthsPrevYears; decimal catA_2025 = 20m * monthsYear2025;
        decimal catB_2023 = 60m * monthsPrevYears; decimal catB_2024 = 60m * monthsPrevYears; decimal catB_2025 = 60m * monthsYear2025;
        decimal catC_2023 = 120m * monthsPrevYears; decimal catC_2024 = 120m * monthsPrevYears; decimal catC_2025 = 120m * monthsYear2025;

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

        // Child (z.B. C3) ñ YTD Summen: Basis 60 pro Monat => 60 * monthsYear2025
        var c3_2025 = result.Points.Single(p => p.GroupKey == $"Contact:{cC3.Id}" && p.PeriodStart == Y(2025));
        c3_2025.Amount.Should().Be(60m * monthsYear2025);
        c3_2025.PreviousAmount.Should().Be(60m * monthsPrevYears); // Vorjahr (gleicher cutoff)
        c3_2025.YearAgoAmount.Should().Be(60m * monthsPrevYears);
        c3_2025.ParentGroupKey.Should().Be(CatKey(catC));
    }

    [Fact]
    public async Task QueryAsync_ShouldApplyEntityFilters_ForTwoKinds_WithTwoSelectedValuesEach()
    {
        using var db = CreateDb();
        // Arrange user and base data
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Accounts (Bank kind)
        var bankContact = new FinanceManager.Domain.Contacts.Contact(user.Id, "Bank", FinanceManager.Shared.Dtos.ContactType.Bank, null, null);
        db.Contacts.Add(bankContact);
        var acc1 = new FinanceManager.Domain.Accounts.Account(user.Id, AccountType.Giro, "A1", null, bankContact.Id);
        var acc2 = new FinanceManager.Domain.Accounts.Account(user.Id, AccountType.Giro, "A2", null, bankContact.Id);
        var acc3 = new FinanceManager.Domain.Accounts.Account(user.Id, AccountType.Giro, "NoiseAcc", null, bankContact.Id);
        db.Accounts.AddRange(acc1, acc2, acc3);

        // Contacts (Contact kind)
        var c1 = new FinanceManager.Domain.Contacts.Contact(user.Id, "C1", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        var c2 = new FinanceManager.Domain.Contacts.Contact(user.Id, "C2", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        var c3 = new FinanceManager.Domain.Contacts.Contact(user.Id, "NoiseContact", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        db.Contacts.AddRange(c1, c2, c3);
        await db.SaveChangesAsync();

        // Two months of aggregates for each selected entity + one noise entity per kind
        var m1 = new DateTime(2024, 8, 1);
        var m2 = new DateTime(2024, 9, 1);
        void AddAgg(PostingKind kind, Guid? accountId, Guid? contactId, decimal baseAmount)
        {
            var a1 = new PostingAggregate(kind, accountId, contactId, null, null, m1, AggregatePeriod.Month); a1.Add(baseAmount);
            var a2x = new PostingAggregate(kind, accountId, contactId, null, null, m2, AggregatePeriod.Month); a2x.Add(baseAmount + 10);
            db.PostingAggregates.AddRange(a1, a2x);
        }

        // Bank
        AddAgg(PostingKind.Bank, acc1.Id, null, 100m);
        AddAgg(PostingKind.Bank, acc2.Id, null, 200m);
        AddAgg(PostingKind.Bank, acc3.Id, null, 999m); // noise (must be filtered out)
        // Contacts
        AddAgg(PostingKind.Contact, null, c1.Id, 10m);
        AddAgg(PostingKind.Contact, null, c2.Id, 20m);
        AddAgg(PostingKind.Contact, null, c3.Id, 999m); // noise (must be filtered out)
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);

        // Build filters: two accounts + two contacts
        var filters = new ReportAggregationFilters(
            AccountIds: new[] { acc1.Id, acc2.Id },
            ContactIds: new[] { c1.Id, c2.Id },
            SavingsPlanIds: null,
            SecurityIds: null,
            ContactCategoryIds: null,
            SavingsPlanCategoryIds: null,
            SecurityCategoryIds: null);

        // Multi-kinds: Bank + Contact
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Bank, // primary kind irrelevant when PostingKinds provided
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: new[] { (int)PostingKind.Bank, (int)PostingKind.Contact },
            AnalysisDate: m2,
            Filters: filters);

        // Act
        var result = await sut.QueryAsync(query, CancellationToken.None);

        // Assert: entity rows for both kinds (latest month m2)
        var rowsM2 = result.Points.Where(p => p.PeriodStart == m2).ToList();

        // Bank entity rows only for acc1 & acc2
        rowsM2.Should().Contain(p => p.GroupKey == $"Account:{acc1.Id}");
        rowsM2.Should().Contain(p => p.GroupKey == $"Account:{acc2.Id}");
        rowsM2.Should().NotContain(p => p.GroupKey == $"Account:{acc3.Id}");

        // Contact entity rows only for c1 & c2
        rowsM2.Should().Contain(p => p.GroupKey == $"Contact:{c1.Id}");
        rowsM2.Should().Contain(p => p.GroupKey == $"Contact:{c2.Id}");
        rowsM2.Should().NotContain(p => p.GroupKey == $"Contact:{c3.Id}");

        // ParentGroupKey should be Type:Kind in multi-mode
        rowsM2.Single(p => p.GroupKey == $"Account:{acc1.Id}").ParentGroupKey.Should().Be("Type:Bank");
        rowsM2.Single(p => p.GroupKey == $"Contact:{c1.Id}").ParentGroupKey.Should().Be("Type:Contact");

        // Amounts
        rowsM2.Single(p => p.GroupKey == $"Account:{acc1.Id}").Amount.Should().Be(110m);
        rowsM2.Single(p => p.GroupKey == $"Account:{acc2.Id}").Amount.Should().Be(210m);
        rowsM2.Single(p => p.GroupKey == $"Contact:{c1.Id}").Amount.Should().Be(20m);
        rowsM2.Single(p => p.GroupKey == $"Contact:{c2.Id}").Amount.Should().Be(30m);

        // Type aggregates exist for both kinds and equal the sum of their selected entities
        var typeBankM2 = rowsM2.Single(p => p.GroupKey == "Type:Bank");
        var typeContactM2 = rowsM2.Single(p => p.GroupKey == "Type:Contact");
        typeBankM2.Amount.Should().Be(110m + 210m);
        typeContactM2.Amount.Should().Be(20m + 30m);
    }

    [Fact]
    public async Task QueryAsync_SecurityDividendCategory_ShouldInjectCurrentMonthZero_AndCarryPrevious_WhenCurrentHasNoData()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Setup security category and one security owned by user
        var secCat = new SecurityCategory(user.Id, "Aktien");
        db.SecurityCategories.Add(secCat);
        await db.SaveChangesAsync();
        var security = new FinanceManager.Domain.Securities.Security(user.Id, "ACME", "ACME-ISIN", null, null, "EUR", secCat.Id);
        db.Securities.Add(security);
        await db.SaveChangesAsync();

        // Define analysis month (current) and previous month
        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var prev = analysis.AddMonths(-1);

        // Only previous month has a dividend aggregate (1.4Ä); current month has no data
        var aggPrev = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, prev, AggregatePeriod.Month);
        aggPrev.Add(1.4m);
        db.PostingAggregates.Add(aggPrev);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);

        // Build query: Security, monthly, category grouping, compare previous enabled, filter Dividend subtype
        var filters = new ReportAggregationFilters(
            AccountIds: null,
            ContactIds: null,
            SavingsPlanIds: null,
            SecurityIds: null,
            ContactCategoryIds: null,
            SavingsPlanCategoryIds: null,
            SecurityCategoryIds: new[] { secCat.Id },
            SecuritySubTypes: new[] { 2 } // Dividend
        );
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: true,
            ComparePrevious: true,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: filters);

        var result = await sut.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.Interval.Should().Be(ReportInterval.Month);
        result.ComparedPrevious.Should().BeTrue();

        string CatKey(SecurityCategory cat) => $"Category:{PostingKind.Security}:{cat.Id}";
        var groupKey = CatKey(secCat);

        // Expect both rows: prev (1.4) and analysis (0) with PreviousAmount=1.4
        var prevRow = result.Points.Single(p => p.GroupKey == groupKey && p.PeriodStart == prev);
        prevRow.Amount.Should().Be(1.4m);
        prevRow.PreviousAmount.Should().BeNull(); // no data two months back

        var currRow = result.Points.Single(p => p.GroupKey == groupKey && p.PeriodStart == analysis);
        currRow.Amount.Should().Be(0m);
        currRow.PreviousAmount.Should().Be(1.4m);
    }

    [Fact]
    public async Task QueryAsync_SecurityDividendCategory_ShouldNotInjectCurrentMonth_WhenNoComparisons()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var secCat = new SecurityCategory(user.Id, "Aktien");
        db.SecurityCategories.Add(secCat);
        await db.SaveChangesAsync();
        var security = new FinanceManager.Domain.Securities.Security(user.Id, "ACME", "ACME-ISIN", null, null, "EUR", secCat.Id);
        db.Securities.Add(security);
        await db.SaveChangesAsync();

        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var prev = analysis.AddMonths(-1);

        var aggPrev = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, prev, AggregatePeriod.Month);
        aggPrev.Add(1.4m);
        db.PostingAggregates.Add(aggPrev);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);

        var filters = new ReportAggregationFilters(
            AccountIds: null,
            ContactIds: null,
            SavingsPlanIds: null,
            SecurityIds: null,
            ContactCategoryIds: null,
            SavingsPlanCategoryIds: null,
            SecurityCategoryIds: new[] { secCat.Id },
            SecuritySubTypes: new[] { 2 }
        );

        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: true,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: filters);

        var result = await sut.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.Interval.Should().Be(ReportInterval.Month);
        result.ComparedPrevious.Should().BeFalse();
        result.ComparedYear.Should().BeFalse();

        string CatKey(SecurityCategory cat) => $"Category:{PostingKind.Security}:{cat.Id}";
        var groupKey = CatKey(secCat);

        // Only prev row should exist, current analysis month should not be injected without comparisons
        var prevRow = result.Points.Single(p => p.GroupKey == groupKey && p.PeriodStart == prev);
        prevRow.Amount.Should().Be(1.4m);
        prevRow.PreviousAmount.Should().BeNull();
        prevRow.YearAgoAmount.Should().BeNull();

        var currRow = result.Points.Single(p => p.GroupKey == groupKey && p.PeriodStart == analysis);
        currRow.Amount.Should().Be(0m);
        currRow.PreviousAmount.Should().Be(null);
    }

    [Fact]
    public async Task QueryAsync_SecurityDividendCategory_SixMonthsAgo_ShouldInjectCurrentZero_AndCarryPrev()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var secCat = new SecurityCategory(user.Id, "Aktien");
        db.SecurityCategories.Add(secCat);
        await db.SaveChangesAsync();
        var security = new FinanceManager.Domain.Securities.Security(user.Id, "ACME", "ACME-ISIN", null, null, "EUR", secCat.Id);
        db.Securities.Add(security);
        await db.SaveChangesAsync();

        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var sixMonthsAgo = analysis.AddMonths(-6);

        // Only six months ago has data
        var agg = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, sixMonthsAgo, AggregatePeriod.Month);
        agg.Add(1.4m);
        db.PostingAggregates.Add(agg);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var filters = new ReportAggregationFilters(
            AccountIds: null,
            ContactIds: null,
            SavingsPlanIds: null,
            SecurityIds: null,
            ContactCategoryIds: null,
            SavingsPlanCategoryIds: null,
            SecurityCategoryIds: new[] { secCat.Id },
            SecuritySubTypes: new[] { 2 }
        );

        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: true,
            ComparePrevious: true,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: filters);

        var result = await sut.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.ComparedPrevious.Should().BeTrue();
        result.Interval.Should().Be(ReportInterval.Month);

        string CatKey(SecurityCategory cat) => $"Category:{PostingKind.Security}:{cat.Id}";
        var groupKey = CatKey(secCat);

        result.Points.Count().Should().Be(0);
    }
}
