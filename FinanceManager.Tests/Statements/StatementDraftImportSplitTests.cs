using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain.Users;
using FinanceManager.Domain.Contacts; // hinzugefügt
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Application.Aggregates;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftImportSplitTests
{
    private sealed class NoOpAggregateService : IPostingAggregateService
    {
        public Task UpsertForPostingAsync(FinanceManager.Domain.Postings.Posting posting, CancellationToken ct) => Task.CompletedTask;
        public Task RebuildForUserAsync(Guid userId, Action<int, int> progressCallback, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestReader : IStatementFileReader
    {
        private readonly IReadOnlyList<StatementMovement> _movements;
        private readonly string _desc;
        public TestReader(IReadOnlyList<StatementMovement> movements, string desc="Test")
        {
            _movements = movements;
            _desc = desc;
        }
        public StatementParseResult? Parse(string fileName, byte[] fileBytes)
        {
            return new StatementParseResult(new StatementHeader
            {
                AccountNumber = "123",
                Description = _desc,
                IBAN = null
            }, _movements);
        }
        public StatementParseResult? ParseDetails(string originalFileName, byte[] fileBytes) => Parse(originalFileName, fileBytes);
    }

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new AppDbContext(opts);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static User AddUser(AppDbContext db, ImportSplitMode mode, int max, int? threshold)
    {
        var u = new User("tester","hash", false);
        typeof(FinanceManager.Domain.Entity).GetProperty("Id")!.SetValue(u, Guid.NewGuid());
        u.SetImportSplitSettings(mode, max, threshold);
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    private static void AddRequiredContacts(AppDbContext db, Guid userId)
    {
        // Mindestkontakt: Self (wird von ClassifyInternalAsync vorausgesetzt)
        if (!db.Contacts.Any(c => c.OwnerUserId == userId && c.Type == ContactType.Self))
        {
            db.Contacts.Add(new Contact(userId, "Me", ContactType.Self, null));
            db.SaveChanges();
        }
    }

    private static List<StatementMovement> BuildMovements(int count, DateTime startDate)
    {
        var list = new List<StatementMovement>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new StatementMovement
            {
                BookingDate = startDate.AddDays(i),
                ValutaDate = startDate.AddDays(i),
                Amount = i + 1,
                Subject = $"S{i}",
                Counterparty = "C",
                CurrencyCode = "EUR",
                PostingDescription = "P"
            });
        }
        return list;
    }

    private async Task<List<StatementDraftDto>> RunAsync(ImportSplitMode mode, int max, int? threshold, int movements, int monthsSpan)
    {
        using var db = CreateDb();
        var user = AddUser(db, mode, max, threshold);
        AddRequiredContacts(db, user.Id); // neu
        var start = new DateTime(2025, 1, 15);
        var data = BuildMovements(movements, start);

        // Neue Logik: Verteilt Bewegungen über (monthsSpan + 1) Monate zyklisch,
        // statt jeden Eintrag um 32 Tage zu verschieben (was zu zu vielen Monaten führte).
        if (monthsSpan > 0)
        {
            int monthsToUse = monthsSpan + 1; // z.B. monthsSpan=2 => 3 Monate
            var baseMonthStart = new DateTime(start.Year, start.Month, 1);
            for (int i = 0; i < data.Count; i++)
            {
                int monthOffset = i % monthsToUse;
                int dayInMonth = (i / monthsToUse) % 28; // Begrenzen damit alle Monate gültige Tage haben
                var booking = baseMonthStart.AddMonths(monthOffset).AddDays(dayInMonth);
                data[i].BookingDate = booking;
                data[i].ValutaDate = booking;
            }
        }
        else
        {
            for (int i = 0; i < data.Count; i++)
            {
                data[i].BookingDate = start.AddDays(i);
                data[i].ValutaDate = data[i].BookingDate;
            }
        }

        var reader = new TestReader(data);
        var svc = new StatementDraftService(db, new NoOpAggregateService(), new[] { reader });
        var results = new List<StatementDraftDto>();
        await foreach (var d in svc.CreateDraftAsync(user.Id, "file.dat", Array.Empty<byte>(), CancellationToken.None))
        {
            results.Add(d);
        }
        return results;
    }

    [Fact]
    public async Task FixedSize_Should_Chunk_By_Max()
    {
        var drafts = await RunAsync(ImportSplitMode.FixedSize, max: 50, threshold: null, movements: 120, monthsSpan:0);
        drafts.Count.Should().Be(3);
        drafts[0].Entries.Count.Should().Be(50);
        drafts[1].Entries.Count.Should().Be(50);
        drafts[2].Entries.Count.Should().Be(20);
        drafts[0].Description.Should().NotBeNull();
    }

    [Fact]
    public async Task Monthly_Should_Group_By_Month()
    {
        var drafts = await RunAsync(ImportSplitMode.Monthly, max: 200, threshold: null, movements: 3, monthsSpan:2);
        drafts.Count.Should().Be(3);
        drafts.Select(d => d.Description).All(d => d!.Contains("2025-")).Should().BeTrue();
    }

    [Fact]
    public async Task MonthlyOrFixed_Should_Use_Fixed_When_Under_Threshold()
    {
        var drafts = await RunAsync(ImportSplitMode.MonthlyOrFixed, max: 100, threshold: 150, movements: 120, monthsSpan:2);
        drafts.Count.Should().Be(2);
        drafts.Any(d => d.Description != null && d.Description.Contains("2025-01")).Should().BeFalse();
    }

    [Fact]
    public async Task MonthlyOrFixed_Should_Use_Monthly_When_Above_Threshold()
    {
        var drafts = await RunAsync(ImportSplitMode.MonthlyOrFixed, max: 60, threshold: 100, movements: 130, monthsSpan:2);
        drafts.Count.Should().Be(3);
        drafts.All(d => d.Description!.Contains("2025-")).Should().BeTrue();
    }
}
