using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Domain.Contacts;
using Bunit; // added for Self contact

namespace FinanceManager.Tests.Statements;

/// <summary>
/// Tests the import split logic (CreateDraftAsync) for fixed, monthly and hybrid modes.
/// NOTE: MinEntriesPerDraft (new feature) is NOT yet applied by the implementation – tests document current behaviour (TDD baseline).
/// </summary>
public sealed class StatementDraftImportSplitTests
{
    private sealed record ImportedDraft(Guid Id, int EntryCount, string? Description);

    private static (StatementDraftService sut, AppDbContext db, SqliteConnection conn, Guid userId) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var user = new User("importer", "hash", false);
        db.Users.Add(user);
        // ensure required Self contact exists for classification
        db.Contacts.Add(new Contact(user.Id, "Me", ContactType.Self, null));
        db.SaveChanges();
        var svc = new StatementDraftService(db, new PostingAggregateService(db));
        return (svc, db, conn, user.Id);
    }

    private static string BuildBackupPayload(IEnumerable<(DateTime date, decimal amount, string subject)> lines, string? iban = "DE00", string? description = "Test")
    {
        var sb = new StringBuilder();
        sb.AppendLine("{\"Type\":\"Backup\",\"Version\":2}");
        sb.Append("{ \"BankAccounts\": [ { \"IBAN\": \"").Append(iban).Append("\"} ], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [");
        bool first = true; int id = 1;
        foreach (var l in lines)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"Id\": ").Append(id++).Append(",")
              .Append("\"PostingDate\": \"").Append(l.date.ToString("yyyy-MM-ddTHH:mm:ss")).Append("\",")
              .Append("\"ValutaDate\": \"").Append(l.date.ToString("yyyy-MM-ddTHH:mm:ss")).Append("\",")
              .Append("\"PostingDescription\": \"Lastschrift\",")
              .Append("\"SourceName\": \"SRC\",")
              .Append("\"Description\": \"").Append(l.subject).Append("\",")
              .Append("\"CurrencyCode\": \"EUR\",")
              .Append("\"Amount\": ").Append(l.amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append("\"CreatedAt\": \"").Append(l.date.ToString("yyyy-MM-ddTHH:mm:ss")).Append("\"}");
        }
        sb.Append("], \"Description\": \"").Append(description).Append("\" }");
        return sb.ToString();
    }

    private static async Task<List<ImportedDraft>> ImportAsync(StatementDraftService sut, AppDbContext db, Guid userId, string payload)
    {
        var drafts = new List<ImportedDraft>();
        var bytes = Encoding.UTF8.GetBytes(payload);
        await foreach (var d in sut.CreateDraftAsync(userId, "import.csv", bytes, CancellationToken.None))
        {
            drafts.Add(new ImportedDraft(d.DraftId, d.Entries.Count, d.Description));
        }
        return drafts;
    }

    [Fact]
    public async Task FixedSizeMode_ShouldChunk_ByMaxEntries()
    {
        var (sut, db, conn, user) = Create();
        var lines = Enumerable.Range(0, 7).Select(i => (new DateTime(2024, 3, 10).AddDays(i), 10m + i, $"L{i}"));
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.FixedSize, 3, null, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(3);
        drafts.Select(d => d.EntryCount).Should().ContainInOrder(3,3,1);
        drafts[0].Description.Should().Contain("(Teil 1)");
        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeFalse();
        conn.Dispose();
    }

    [Fact]
    public async Task MonthlyMode_ShouldProduceOneDraftPerMonth()
    {
        var (sut, db, conn, user) = Create();
        var lines = new List<(DateTime, decimal, string)>
        {
            (new DateTime(2024,1,5), 10m, "A"),
            (new DateTime(2024,1,6), 11m, "B"),
            (new DateTime(2024,2,1), 12m, "C"),
            (new DateTime(2024,2,2), 13m, "D")
        };
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.Monthly, 100, null, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(2);
        drafts.All(d => d.Description!.EndsWith("2024-01") || d.Description!.EndsWith("2024-02")).Should().BeTrue();
        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task MonthlyMode_ShouldSplitMonth_WhenExceedsMax()
    {
        var (sut, db, conn, user) = Create();
        var lines = Enumerable.Range(0,5).Select(i => (new DateTime(2024,4,1).AddDays(i), 1m + i, $"X{i}"));
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.Monthly, 2, null, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(3);
        drafts.Select(d => d.EntryCount).Should().ContainInOrder(2,2,1);
        drafts[0].Description.Should().Contain("(Teil 1)");
        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task HybridMode_UsesMonthly_WhenTotalGreaterThanThreshold_CurrentImplementation()
    {
        var (sut, db, conn, user) = Create();
        var lines = Enumerable.Range(0,6).Select(i => (new DateTime(2024,1,1).AddDays(i), 1m, $"J{i}"))
            .Concat(Enumerable.Range(0,6).Select(i => (new DateTime(2024,2,1).AddDays(i), 1m, $"K{i}")));
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.MonthlyOrFixed, 8, 8, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(2);
        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task HybridMode_UsesFixed_WhenTotalNotGreaterThanThreshold_CurrentImplementation()
    {
        var (sut, db, conn, user) = Create();
        var lines = Enumerable.Range(0,6).Select(i => (new DateTime(2024,3,1).AddDays(i), 1m, $"H{i}"));
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.MonthlyOrFixed, 10, 10, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(1);
        drafts[0].EntryCount.Should().Be(6);
        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeFalse();
        conn.Dispose();
    }

    [Fact]
    public async Task MonthlyMode_SmallMonth_RemainsStandalone_BeforeMinEntriesLogic()
    {
        var (sut, db, conn, user) = Create();
        var lines = new List<(DateTime, decimal, string)>();
        lines.Add((new DateTime(2024,5,15), 5m, "Solo"));
        lines.AddRange(Enumerable.Range(0,9).Select(i => (new DateTime(2024,6,1).AddDays(i), 1m + i, $"M{i}")));
        var payload = BuildBackupPayload(lines);
        var u = await db.Users.SingleAsync();
        u.SetImportSplitSettings(ImportSplitMode.Monthly, 50, null, 1);
        await db.SaveChangesAsync();
        var drafts = await ImportAsync(sut, db, user, payload);
        drafts.Should().HaveCount(2);
        drafts.Select(d => d.EntryCount).Order().Should().Equal(1,9);
        conn.Dispose();
    }

    public static IEnumerable<object[]> MonthlyMinEntriesMergeCases =>
        new[]
        {
            new object[] { new[] { 3, 20 }, 5, new[] { 23 } },          // kleiner Monat zuerst
            new object[] { new[] { 20, 3 }, 5, new[] { 23 } },          // kleiner Monat zuletzt
            new object[] { new[] { 3, 20, 20 }, 5, new[] { 23, 20 } },      // kleiner Monat am Anfang (3 von 3)
            new object[] { new[] { 20, 3, 20 }, 5, new[] { 20, 23 } },      // kleiner Monat in der Mitte
            new object[] { new[] { 20, 20, 3 }, 5, new[] { 20, 23 } },      // kleiner Monat am Ende,
            new object[] { new[] { 1, 1, 1, 1, 1, 1, 20 }, 5, new[] { 5, 21 } },
            new object[] { new[] { 19, 1, 1, 19 }, 5, new[] { 20, 20 } },
        };

    [Theory]
    [MemberData(nameof(MonthlyMinEntriesMergeCases))]
    public async Task MonthlyMode_ShouldMergeSmallMonths_WhenBelowMinEntries(int[] monthEntryCounts, int minEntriesPerDraft, int[] expectedDrafts)
    {
        var (sut, db, conn, user) = Create();

        // Arrange: Monate ab 2024-01
        var all = new List<(DateTime date, decimal amount, string subject)>();
        var start = new DateTime(2024, 1, 1);
        for (int m = 0; m < monthEntryCounts.Length; m++)
        {
            var monthStart = start.AddMonths(m);
            for (int i = 0; i < monthEntryCounts[m]; i++)
            {
                all.Add((monthStart.AddDays(i), 1m, $"M{m:D2}_{i:D2}"));
            }
        }

        var payload = BuildBackupPayload(all);
        var u = await db.Users.SingleAsync();

        // Max groß genug wählen, damit kein Split wegen Max greift
        u.SetImportSplitSettings(ImportSplitMode.Monthly, maxEntriesPerDraft: 500, monthlySplitThreshold: null, minEntriesPerDraft: minEntriesPerDraft);
        await db.SaveChangesAsync();

        // Act
        var drafts = await ImportAsync(sut, db, user, payload);

        // Assert
        drafts.Should().HaveCount(expectedDrafts.Length);

        drafts.Select(d => d.EntryCount).Should().BeEquivalentTo(expectedDrafts);

        sut.LastImportSplitInfo!.EffectiveMonthly.Should().BeTrue();
        conn.Dispose();
    }
}
