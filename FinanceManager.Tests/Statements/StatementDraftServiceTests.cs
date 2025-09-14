using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftServiceTests
{
    private sealed class TestCurrentUserService : FinanceManager.Application.ICurrentUserService
    {
        public Guid UserId { get; internal set; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
        public string? PreferredLanguage => null;
    }
    private static (StatementDraftService sut, AppDbContext db, Guid ownerId) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var owner = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(owner);
        db.SaveChanges();
        var ownerContact = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(ownerContact);
        db.SaveChanges();

        var current = new TestCurrentUserService()
        {
            UserId = owner.Id
        };

        var sut = new StatementDraftService(db);
        return (sut, db, owner.Id);
    }

    [Fact]
    public async Task CreateDraftAsync_ShouldReturnEntries_AndAutoDetectAccount_WhenSingleAccount()
    {
        var (sut, db, owner) = Create();
        db.Accounts.Add(new Account(owner, FinanceManager.Domain.AccountType.Giro, "Test", null, Guid.NewGuid()));
        db.SaveChanges();

        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var draft in sut.CreateDraftAsync(owner, "file.csv", bytes, CancellationToken.None))
        {
            draft.Entries.Should().HaveCount(1);
            draft.DetectedAccountId.Should().NotBeNull();
            draft.OriginalFileName.Should().Be("file.csv");
            counter++;
        }
        counter.Should().Be(1);
    }

    [Fact]
    public async Task CreateDraftAsync_ShouldHaveNullDetectedAccount_WhenNoAccounts()
    {
        var (sut, _, owner) = Create();

        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"DE123456\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var draft in sut.CreateDraftAsync(owner, "f.csv", bytes, CancellationToken.None))
        {
            draft.DetectedAccountId.Should().BeNull();
            counter++;
        }
        counter.Should().Be(1);        
    }

    [Fact]
    public async Task CommitAsync_ShouldReturnResult()
    {
        var (sut, db, owner) = Create();
        var accountId = Guid.NewGuid();

        // Arrange: Account und Draft anlegen
        db.Accounts.Add(new Account(owner, FinanceManager.Domain.AccountType.Giro, "Testkonto", null, Guid.NewGuid()));
        db.SaveChanges();

        var draft = new FinanceManager.Domain.Statements.StatementDraft(owner, "file.csv", "", null);
        draft.AddEntry(DateTime.UtcNow.Date.AddDays(-2), 123.45m, "Sample Payment A");
        draft.AddEntry(DateTime.UtcNow.Date.AddDays(-1), -49.99m, "Sample Debit B");
        db.StatementDrafts.Add(draft);
        db.SaveChanges();

        // Act
        var result = await sut.CommitAsync(draft.Id, owner, db.Accounts.Single().Id, FinanceManager.Domain.ImportFormat.Csv, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.TotalEntries.Should().Be(2);
    }
}
