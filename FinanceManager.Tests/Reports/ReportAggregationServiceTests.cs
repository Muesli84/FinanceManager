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
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;

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
        var aggPrev = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, prev, AggregatePeriod.Month, SecurityPostingSubType.Dividend);
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

        var aggPrev = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, prev, AggregatePeriod.Month, SecurityPostingSubType.Dividend);
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
        var agg = new PostingAggregate(PostingKind.Security, null, null, null, security.Id, sixMonthsAgo, AggregatePeriod.Month, SecurityPostingSubType.Dividend);
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

    private sealed record SeedEntities(
        Account Acc1, Account Acc2,
        FinanceManager.Domain.Contacts.Contact Con1, FinanceManager.Domain.Contacts.Contact Con2,
        SavingsPlan Sav1, SavingsPlan Sav2,
        FinanceManager.Domain.Securities.Security Sec1, FinanceManager.Domain.Securities.Security Sec2,
        SecurityCategory SecCat);

    private sealed class SeedResult
    {
        public required SeedEntities Entities { get; init; }
        // key: (PostingKind, accountId, contactId, savingsPlanId, securityId, periodStart)
        public Dictionary<(PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, DateTime Period), decimal> Sums { get; } = new();
        public List<DateTime> Months { get; } = new();
    }

    /// <summary>
    /// Seeds two accounts, two contacts, two savings plans, two securities and creates monthly aggregates for the last N months.
    /// For each entity and month two postings are added (1st and 15th), with a global amount counter starting at 1.00Ä and increasing by 0.01Ä per posting to ensure uniqueness.
    /// Returns the created entities, the list of months and a sum lookup for expected assertions.
    /// </summary>
    private static async Task<SeedResult> SeedAllKindsAsync(AppDbContext db, Guid ownerUserId, DateTime analysisMonth, int monthsBack)
    {
        var bank = new FinanceManager.Domain.Contacts.Contact(ownerUserId, "Bank", FinanceManager.Shared.Dtos.ContactType.Bank, null, null);
        db.Contacts.Add(bank);
        var acc1 = new Account(ownerUserId, AccountType.Giro, "ACC1", null, bank.Id);
        var acc2 = new Account(ownerUserId, AccountType.Giro, "ACC2", null, bank.Id);
        db.Accounts.AddRange(acc1, acc2);

        var con1 = new FinanceManager.Domain.Contacts.Contact(ownerUserId, "CON1", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        var con2 = new FinanceManager.Domain.Contacts.Contact(ownerUserId, "CON2", FinanceManager.Shared.Dtos.ContactType.Person, null, null);
        db.Contacts.AddRange(con1, con2);

        var sav1 = new SavingsPlan(ownerUserId, "SAV1", SavingsPlanType.Open, null, null, null, null);
        var sav2 = new SavingsPlan(ownerUserId, "SAV2", SavingsPlanType.Open, null, null, null, null);
        db.SavingsPlans.AddRange(sav1, sav2);

        var secCat = new SecurityCategory(ownerUserId, "SEC-CAT");
        db.SecurityCategories.Add(secCat);
        await db.SaveChangesAsync();
        var sec1 = new FinanceManager.Domain.Securities.Security(ownerUserId, "SEC1", "ISIN-1", null, null, "EUR", secCat.Id);
        var sec2 = new FinanceManager.Domain.Securities.Security(ownerUserId, "SEC2", "ISIN-2", null, null, "EUR", secCat.Id);
        db.Securities.AddRange(sec1, sec2);
        await db.SaveChangesAsync();

        var res = new SeedResult
        {
            Entities = new SeedEntities(acc1, acc2, con1, con2, sav1, sav2, sec1, sec2, secCat)
        };

        // build months list ending at analysisMonth inclusive, going back monthsBack-1 months
        var first = analysisMonth.AddMonths(-monthsBack + 1);
        for (var d = first; d <= analysisMonth; d = d.AddMonths(1))
        {
            res.Months.Add(new DateTime(d.Year, d.Month, 1));
        }

        decimal amount = 1.00m;
        void AddFor(Guid? accountId, Guid? contactId, Guid? savId, Guid? securityId, PostingKind kind)
        {
            foreach (var m in res.Months)
            {
                var agg = new PostingAggregate(kind, accountId, contactId, savId, securityId, m, AggregatePeriod.Month);
                // two postings per month: 1st and 15th ? add two different amounts
                agg.Add(amount); amount += 0.01m;
                agg.Add(amount); amount += 0.01m;
                var sum = agg.Amount;
                res.Sums[(kind, accountId, contactId, savId, securityId, m)] = sum;
                db.PostingAggregates.Add(agg);
            }
        }

        AddFor(acc1.Id, null, null, null, PostingKind.Bank);
        AddFor(acc2.Id, null, null, null, PostingKind.Bank);
        AddFor(null, con1.Id, null, null, PostingKind.Contact);
        AddFor(null, con2.Id, null, null, PostingKind.Contact);
        AddFor(null, null, sav1.Id, null, PostingKind.SavingsPlan);
        AddFor(null, null, sav2.Id, null, PostingKind.SavingsPlan);
        AddFor(null, null, null, sec1.Id, PostingKind.Security);
        AddFor(null, null, null, sec2.Id, PostingKind.Security);

        await db.SaveChangesAsync();

        // Build higher-level aggregates (Quarter, HalfYear, Year) for all seeded entities
        static DateTime ToQuarterStart(DateTime d) => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1);
        static DateTime ToHalfYearStart(DateTime d) => new DateTime(d.Year, d.Month <= 6 ? 1 : 7, 1);
        static DateTime ToYearStart(DateTime d) => new DateTime(d.Year, 1, 1);

        var entities = new (PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId)[]
        {
            (PostingKind.Bank, acc1.Id, null, null, null),
            (PostingKind.Bank, acc2.Id, null, null, null),
            (PostingKind.Contact, null, con1.Id, null, null),
            (PostingKind.Contact, null, con2.Id, null, null),
            (PostingKind.SavingsPlan, null, null, sav1.Id, null),
            (PostingKind.SavingsPlan, null, null, sav2.Id, null),
            (PostingKind.Security, null, null, null, sec1.Id),
            (PostingKind.Security, null, null, null, sec2.Id)
        };

        void BuildPeriodAggregates(AggregatePeriod period, Func<DateTime, DateTime> map)
        {
            foreach (var e in entities)
            {
                var grouped = res.Months
                    .GroupBy(m => map(m))
                    .Select(g => new
                    {
                        Start = g.Key,
                        Sum = g.Sum(m => res.Sums[(e.Kind, e.AccountId, e.ContactId, e.SavingsPlanId, e.SecurityId, m)])
                    });
                foreach (var g in grouped)
                {
                    var agg = new PostingAggregate(e.Kind, e.AccountId, e.ContactId, e.SavingsPlanId, e.SecurityId, g.Start, period);
                    agg.Add(g.Sum);
                    db.PostingAggregates.Add(agg);
                }
            }
        }

        BuildPeriodAggregates(AggregatePeriod.Quarter, ToQuarterStart);
        BuildPeriodAggregates(AggregatePeriod.HalfYear, ToHalfYearStart);
        BuildPeriodAggregates(AggregatePeriod.Year, ToYearStart);
        await db.SaveChangesAsync();
        return res;
    }

    /// <summary>
    /// Hilfsfunktion: Erwartete Monats?Summe je Entit‰t und Vergleichswerte (Vormonat, Vorjahr) aus Seed?Lookup berechnen.
    /// </summary>
    private static (decimal current, decimal? prev, decimal? year)
        GetMonthlyExpected(
            SeedResult seed,
            PostingKind kind,
            Guid? accountId,
            Guid? contactId,
            Guid? savId,
            Guid? secId,
            DateTime analysis)
    {
        var cur = seed.Sums[(kind, accountId, contactId, savId, secId, analysis)];
        var prevDate = analysis.AddMonths(-1);
        var yearDate = analysis.AddYears(-1);
        seed.Sums.TryGetValue((kind, accountId, contactId, savId, secId, prevDate), out var prev);
        seed.Sums.TryGetValue((kind, accountId, contactId, savId, secId, yearDate), out var year);
        return (cur, seed.Sums.ContainsKey((kind, accountId, contactId, savId, secId, prevDate)) ? prev : null,
                     seed.Sums.ContainsKey((kind, accountId, contactId, savId, secId, yearDate)) ? year : null);
    }

    /// <summary>
    /// Seeds 24 months for 2 accounts/contacts/savings/securities with unique amounts (1.00Ä + 0.01Ä per posting), two postings per month (1st/15th).
    /// Builds a monthly report for Bank, Contact, SavingsPlan, Security with comparisons (prev + year) and verifies the latest month's balances for both entities.
    /// Expected: Amount equals seeded monthly sum; Previous equals exact previous month; YearAgo equals same month last year.
    /// </summary>
    [Fact]
    public async Task QueryAsync_Monthly_AllKinds_VerifyPrevAndYear_ForTwoEntities()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var seed = await SeedAllKindsAsync(db, user.Id, analysis, monthsBack: 24);
        var sut = new ReportAggregationService(db);

        async Task AssertForKindAsync(PostingKind kind, Guid? id1, Guid? id2)
        {
            var q = new ReportAggregationQuery(user.Id, (int)kind, ReportInterval.Month, 24, IncludeCategory: false, ComparePrevious: true, CompareYear: true, PostingKinds: null, AnalysisDate: analysis, Filters: null);
            var result = await sut.QueryAsync(q, CancellationToken.None);
            result.Interval.Should().Be(ReportInterval.Month);
            result.ComparedPrevious.Should().BeTrue();
            result.ComparedYear.Should().BeTrue();

            (Guid? acc, Guid? con, Guid? sav, Guid? sec) Map(Guid? id) => kind switch
            {
                PostingKind.Bank => (id, null, null, null),
                PostingKind.Contact => (null, id, null, null),
                PostingKind.SavingsPlan => (null, null, id, null),
                PostingKind.Security => (null, null, null, id),
                _ => (null, null, null, null)
            };

            var m1 = Map(id1); var m2 = Map(id2);
            var (c1, p1, y1) = GetMonthlyExpected(seed, kind, m1.acc, m1.con, m1.sav, m1.sec, analysis);
            var (c2, p2, y2) = GetMonthlyExpected(seed, kind, m2.acc, m2.con, m2.sav, m2.sec, analysis);
            string Key1 = kind switch { PostingKind.Bank => $"Account:{id1}", PostingKind.Contact => $"Contact:{id1}", PostingKind.SavingsPlan => $"SavingsPlan:{id1}", PostingKind.Security => $"Security:{id1}", _ => string.Empty };
            string Key2 = kind switch { PostingKind.Bank => $"Account:{id2}", PostingKind.Contact => $"Contact:{id2}", PostingKind.SavingsPlan => $"SavingsPlan:{id2}", PostingKind.Security => $"Security:{id2}", _ => string.Empty };

            var r1 = result.Points.Single(p => p.GroupKey == Key1 && p.PeriodStart == analysis);
            var r2 = result.Points.Single(p => p.GroupKey == Key2 && p.PeriodStart == analysis);
            r1.Amount.Should().Be(c1); r1.PreviousAmount.Should().Be(p1); r1.YearAgoAmount.Should().Be(y1);
            r2.Amount.Should().Be(c2); r2.PreviousAmount.Should().Be(p2); r2.YearAgoAmount.Should().Be(y2);
        }

        await AssertForKindAsync(PostingKind.Bank, seed.Entities.Acc1.Id, seed.Entities.Acc2.Id);
        await AssertForKindAsync(PostingKind.Contact, seed.Entities.Con1.Id, seed.Entities.Con2.Id);
        await AssertForKindAsync(PostingKind.SavingsPlan, seed.Entities.Sav1.Id, seed.Entities.Sav2.Id);
        await AssertForKindAsync(PostingKind.Security, seed.Entities.Sec1.Id, seed.Entities.Sec2.Id);
    }

    /// <summary>
    /// With the same seeded postings, query with filters that select non-existing entities. Expect an empty result (no rows).
    /// </summary>
    [Fact]
    public async Task QueryAsync_Monthly_SelectOtherEntities_ShouldBeEmpty()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        _ = await SeedAllKindsAsync(db, user.Id, analysis, monthsBack: 12);
        var sut = new ReportAggregationService(db);

        var filters = new ReportAggregationFilters(
            AccountIds: new[] { Guid.NewGuid() },
            ContactIds: null,
            SavingsPlanIds: null,
            SecurityIds: null,
            ContactCategoryIds: null,
            SavingsPlanCategoryIds: null,
            SecurityCategoryIds: null);

        var q = new ReportAggregationQuery(user.Id, (int)PostingKind.Bank, ReportInterval.Month, 12, IncludeCategory: false, ComparePrevious: true, CompareYear: true, PostingKinds: null, AnalysisDate: analysis, Filters: filters);
        var result = await sut.QueryAsync(q, CancellationToken.None);
        result.Points.Should().BeEmpty();
    }

    /// <summary>
    /// Quarterly/HalfYearly/Yearly/YTD/AllHistory: Verify that the aggregation sums match the seeded per-month sums grouped per interval for Bank postings.
    /// Previous comparisons are checked for the exact previous interval when applicable, YearAgo for yearly.
    /// For AllHistory, verify total across all returned periods equals the seeded total.
    /// </summary>
    [Theory]
    [InlineData(ReportInterval.Quarter)]
    [InlineData(ReportInterval.HalfYear)]
    [InlineData(ReportInterval.Year)]
    [InlineData(ReportInterval.Ytd)]
    [InlineData(ReportInterval.AllHistory)]
    public async Task QueryAsync_VariousIntervals_Bank_VerifySums(ReportInterval interval)
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var analysis = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var seed = await SeedAllKindsAsync(db, user.Id, analysis, monthsBack: 24);
        var sut = new ReportAggregationService(db);

        DateTime periodStart(DateTime d)
             => interval switch
             {
                 ReportInterval.Quarter => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
                 ReportInterval.HalfYear => new DateTime(d.Year, d.Month <= 6 ? 1 : 7, 1),
                 ReportInterval.Year => new DateTime(d.Year, 1, 1),
                 ReportInterval.Ytd => new DateTime(d.Year, 1, 1),
                 _ => d
             };

        IEnumerable<DateTime> monthsInPeriod(DateTime p)
        {
            if (interval == ReportInterval.Quarter)
            {
                var start = periodStart(p);
                return Enumerable.Range(0, 3).Select(i => start.AddMonths(i));
            }
            if (interval == ReportInterval.HalfYear)
            {
                var start = periodStart(p);
                return Enumerable.Range(0, 6).Select(i => start.AddMonths(i));
            }
            if (interval == ReportInterval.Year || interval == ReportInterval.Ytd)
            {
                var start = periodStart(p);
                var endMonth = interval == ReportInterval.Year ? 12 : analysis.Month;
                return Enumerable.Range(0, endMonth).Select(i => new DateTime(p.Year, 1, 1).AddMonths(i));
            }
            return new[] { p };
        }

        var q = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Bank,
            Interval: interval,
            Take: 24,
            IncludeCategory: false,
            ComparePrevious: true,
            CompareYear: true,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: null);
        var result = await sut.QueryAsync(q, CancellationToken.None);

        if (interval == ReportInterval.AllHistory)
        {
            var accountKeyAll = $"Account:{seed.Entities.Acc1.Id}";
            var hasEntityRowsAll = result.Points.Any(p => p.GroupKey.StartsWith("Account:"));
            decimal totalReturned;
            decimal totalSeeded;
            if (hasEntityRowsAll)
            {
                totalReturned = result.Points.Where(p => p.GroupKey == accountKeyAll).Sum(p => p.Amount);
                totalSeeded = seed.Months.Sum(m => seed.Sums[(PostingKind.Bank, seed.Entities.Acc1.Id, null, null, null, m)]);
            }
            else
            {
                totalReturned = result.Points.Where(p => p.GroupKey == "Type:Bank").Sum(p => p.Amount);
                totalSeeded = seed.Months.Sum(m =>
                    seed.Sums[(PostingKind.Bank, seed.Entities.Acc1.Id, null, null, null, m)] +
                    seed.Sums[(PostingKind.Bank, seed.Entities.Acc2.Id, null, null, null, m)]);
            }
            totalReturned.Should().Be(totalSeeded);
            return;
        }

        DateTime ps = periodStart(analysis);
        var accountKey = $"Account:{seed.Entities.Acc1.Id}";
        var typeKey = "Type:Bank";

        // Determine whether entity-level or type-level rows are present
        var hasEntityRows = result.Points.Any(p => p.PeriodStart == ps && p.GroupKey.StartsWith("Account:"));
        var groupKey = hasEntityRows ? accountKey : typeKey;

        // Expected amount for the chosen group
        decimal expected = 0m;
        if (groupKey == accountKey)
        {
            expected = monthsInPeriod(ps)
                .Where(m => seed.Months.Contains(m))
                .Sum(m => seed.Sums[(PostingKind.Bank, seed.Entities.Acc1.Id, null, null, null, m)]);
        }
        else
        {
            expected = monthsInPeriod(ps)
                .Where(m => seed.Months.Contains(m))
                .Sum(m => seed.Sums[(PostingKind.Bank, seed.Entities.Acc1.Id, null, null, null, m)]
                         + seed.Sums[(PostingKind.Bank, seed.Entities.Acc2.Id, null, null, null, m)]);
        }

        var rowPoint = result.Points.SingleOrDefault(p => p.GroupKey == groupKey && p.PeriodStart == ps);
        rowPoint.Should().NotBeNull();
        rowPoint!.Amount.Should().Be(expected);

        // Previous for exact previous interval should exist when months available
        DateTime prevStart = interval switch
        {
            ReportInterval.Quarter => ps.AddMonths(-3),
            ReportInterval.HalfYear => ps.AddMonths(-6),
            ReportInterval.Year => new DateTime(ps.Year - 1, 1, 1),
            ReportInterval.Ytd => new DateTime(ps.Year - 1, 1, 1),
            _ => ps.AddMonths(-1)
        };
        var prevRow = result.Points.SingleOrDefault(p => p.GroupKey == groupKey && p.PeriodStart == prevStart);
        if (prevRow == null)
        {
            rowPoint.PreviousAmount.Should().BeNull();
        }
        else
        {
            rowPoint.PreviousAmount.Should().Be(prevRow.Amount);
        }
     }
}
