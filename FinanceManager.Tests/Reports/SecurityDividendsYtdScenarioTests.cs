using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Aggregates;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Reports;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace FinanceManager.Tests.Reports;

public sealed class SecurityDividendsYtdScenarioTests
{
    private readonly ITestOutputHelper _output;

    public SecurityDividendsYtdScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
    public async Task EndToEnd_SecurityDividends_Ytd_WithPrevYear_ShouldMatchExpectedNetAmounts()
    {
        using var db = CreateDb();
        var agg = new PostingAggregateService(db);
        var drafts = new StatementDraftService(db, agg);
        var reports = new ReportAggregationService(db);
        var ct = CancellationToken.None;

        // Owner and base entities
        var user = new FinanceManager.Domain.Users.User("owner","pw",false);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // ensure self contact exists (required by booking service)
        var self = new Contact(user.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        await db.SaveChangesAsync(ct);

        var bankContact = new Contact(user.Id, "ING", ContactType.Bank, null, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync(ct);

        var account = new Account(user.Id, AccountType.Giro, "Giro", null, bankContact.Id);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        // Security: APPLE INC US0378331005
        var sec = new Security(user.Id, "APPLE INC", "US0378331005", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(ct);

        // Create statement draft and assign account
        var draft = new StatementDraft(user.Id, "unit", account.Iban ?? string.Empty, "Test Draft");
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        await drafts.SetAccountAsync(draft.Id, user.Id, account.Id, ct);

        // Helper to add and fully set entry
        async Task<Guid> AddEntryAsync(DateTime bookingDate, decimal amount, string subject, string? recipient, string? desc,
            SecurityTransactionType? txType, decimal? qty, decimal? fee, decimal? tax)
        {
            var dto = await drafts.AddEntryAsync(draft.Id, user.Id, bookingDate, amount, subject, ct);
            Assert.NotNull(dto);
            var entryId = dto!.Entries.Last().Id;
            await drafts.UpdateEntryCoreAsync(draft.Id, entryId, user.Id, bookingDate, bookingDate, amount, subject, recipient, null, desc, ct);
            await drafts.SetEntryContactAsync(draft.Id, entryId, bankContact.Id, user.Id, ct);
            await drafts.SetEntrySecurityAsync(draft.Id, entryId, sec.Id, txType, qty, fee, tax, user.Id, ct);
            return entryId;
        }

        // Entries per specification (amounts in EUR, buy is outflow => negative)
        // 06.08.2024 Buy 10 pcs, fee 9.64
        await AddEntryAsync(new DateTime(2024, 8, 6), -1884.76m,
            "WP-ABRECHNUNG 0351663749001 Kauf ISIN US0378331005 APPLE INC. [Wertpapier: Apple Inc. (US0378331005)]",
            null, "Wertpapierkauf", SecurityTransactionType.Buy, 10m, 9.64m, null);

        // 20.08.2024 Dividend 1.91
        await AddEntryAsync(new DateTime(2024, 8, 20), 1.91m,
            "Zins/Dividende ISIN US0378331005 APPLE INC [Wertpapier: Apple Inc. (US0378331005)]",
            null, "Zins / Dividende WP", SecurityTransactionType.Dividend, null, null, null);

        // 19.11.2024 Dividend 1.68, tax 0.32
        await AddEntryAsync(new DateTime(2024, 11, 19), 1.68m,
            "Zins/Dividende ISIN US0378331005 APPLE INC [Wertpapier: Apple Inc. (US0378331005)]",
            "ING", "Zins / Dividende WP", SecurityTransactionType.Dividend, null, null, 0.32m);

        // 18.02.2025 Dividend 2.01
        await AddEntryAsync(new DateTime(2025, 2, 18), 2.01m,
            "Zins/Dividende ISIN US0378331005 APPLE INC [Wertpapier: Apple Inc. (US0378331005)]",
            "ING", "Zins / Dividende WP", SecurityTransactionType.Dividend, null, null, null);

        // 20.05.2025 Dividend 1.70, tax 0.25
        await AddEntryAsync(new DateTime(2025, 5, 20), 1.70m,
            "Zins/Dividende ISIN US0378331005 APPLE INC [Wertpapier: Apple Inc. (US0378331005)]",
            "ING", "Zins / Dividende WP", SecurityTransactionType.Dividend, null, null, 0.25m);

        // 19.08.2025 Dividend 1.64, tax 0.24
        await AddEntryAsync(new DateTime(2025, 8, 19), 1.64m,
            "Zins/Dividende ISIN US0378331005 APPLE INC [Wertpapier: Apple Inc. (US0378331005)]",
            "ING", "Zins / Dividende WP", SecurityTransactionType.Dividend, null, null, 0.24m);

        // Validate and book (force warnings if any)
        var bookResult = await drafts.BookAsync(draft.Id, null, user.Id, true, ct);
        Assert.True(bookResult.Success, "draft should be booked successfully");

        // Diagnostic dump: postings, group nets and posting aggregates
        var postings = await db.Postings.AsNoTracking()
            .Where(p => p.Kind == PostingKind.Security && p.SecurityId == sec.Id)
            .OrderBy(p => p.GroupId).ThenBy(p => p.BookingDate)
            .ToListAsync(ct);

        _output.WriteLine("--- Postings for security ---");
        foreach (var p in postings)
        {
            _output.WriteLine($"Id={p.Id}, Group={p.GroupId}, SubType={(int?)p.SecuritySubType}, Amount={p.Amount}, Booking={p.BookingDate:yyyy-MM-dd}, Valuta={p.ValutaDate:yyyy-MM-dd}");
        }

        var groupNets = postings
            .GroupBy(p => p.GroupId)
            .Select(g => new
            {
                Group = g.Key,
                Net = g.Where(x => x.SecuritySubType != null && ((int)x.SecuritySubType == 2 || (int)x.SecuritySubType == 3 || (int)x.SecuritySubType == 4)).Sum(x => x.Amount),
                DividendBooking = g.Where(x => x.SecuritySubType != null && (int)x.SecuritySubType == 2).Select(x => x.BookingDate).FirstOrDefault(),
                Items = g.ToList()
            })
            .ToList();

        _output.WriteLine("--- Group nets ---");
        foreach (var gn in groupNets)
        {
            _output.WriteLine($"Group={gn.Group}, Net={gn.Net}, DividendBooking={(gn.DividendBooking == default ? "(none)" : gn.DividendBooking.ToString("yyyy-MM-dd"))}");
            foreach (var it in gn.Items)
            {
                _output.WriteLine($"  Item: SubType={(int?)it.SecuritySubType}, Amount={it.Amount}, Booking={it.BookingDate:yyyy-MM-dd}");
            }
        }

        var aggs = await db.PostingAggregates.AsNoTracking()
            .Where(a => a.Kind == PostingKind.Security && a.SecurityId == sec.Id)
            .OrderBy(a => a.PeriodStart).ThenBy(a => a.DateKind)
            .ToListAsync(ct);
        _output.WriteLine("--- PostingAggregates for security ---");
        foreach (var a in aggs)
        {
            _output.WriteLine($"Period={a.PeriodStart:yyyy-MM-dd}, DateKind={a.DateKind}, Amount={a.Amount}, SubType={(int?)a.SecuritySubType}");
        }

        // Build YTD report for Security with analysis date 08.10.2025
        var analysis = new DateTime(2025, 10, 8);
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Ytd,
            Take: 24,
            IncludeCategory: false,
            ComparePrevious: true,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            // Filter now restricted to Dividend sub type (2)
            Filters: new ReportAggregationFilters(SecurityIds: new[] { sec.Id }, SecuritySubTypes: new[] { 2 })
        );

        var result = await reports.QueryAsync(query, ct);

        _output.WriteLine("--- Report points ---");
        foreach (var p in result.Points)
        {
            _output.WriteLine($"Period={p.PeriodStart:yyyy-MM-dd}, Group={p.GroupKey}, Amount={p.Amount}, Prev={p.PreviousAmount}");
        }

        Assert.Equal(ReportInterval.Ytd, result.Interval);
        var periodStart = new DateTime(2025, 1, 1);
        var row = result.Points.Single(p => p.GroupKey == $"Security:{sec.Id}" && p.PeriodStart == periodStart);
        // Aggregates are created per date kind (Booking + Valuta) in other flows, but the net-dividend special-case returns net sums once.
        // 2025 net sum = 1.64 + 1.70 + 2.01 = 5.35 (single net)
        Assert.Equal(5.35m, row.Amount);
        // PreviousAmount for YTD (dividends only) should include only the 2024 dividend before November (1.91)
        Assert.Equal(1.91m, row.PreviousAmount);
    }
}
