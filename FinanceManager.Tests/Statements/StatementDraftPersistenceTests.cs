using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftPersistenceTests
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

        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, owner.Id);
    }

    [Fact]
    public async Task GetDraftAsync_ShouldReturnPersistedDraft()
    {
        var (sut, db, owner) = Create();
        var account = new Account(owner, AccountType.Giro, "A", null, Guid.NewGuid());
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"{account.Iban}\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var created in sut.CreateDraftAsync(owner, "x.csv", bytes, CancellationToken.None))
        {
            var fetched = await sut.GetDraftAsync(created.DraftId, owner, CancellationToken.None);
            Assert.NotNull(fetched);
            Assert.Equal(created.Entries.Count, fetched!.Entries.Count);
            counter++;
        }
        Assert.Equal(1, counter);
    }

    [Fact]
    public async Task AddEntryAsync_ShouldAppendEntry()
    {
        var (sut, db, owner) = Create();
        var account = new Account(owner, AccountType.Giro, "A", null, Guid.NewGuid());
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"{account.Iban}\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var draft in sut.CreateDraftAsync(owner, "y.csv", bytes, CancellationToken.None))
        {
            var updated = await sut.AddEntryAsync(draft.DraftId, owner, DateTime.UtcNow.Date, 10m, "Manual", CancellationToken.None);

            Assert.NotNull(updated);
            Assert.Equal(draft.Entries.Count + 1, updated!.Entries.Count);
            Assert.True(updated.Entries.Any(e => e.Subject == "Manual"));
            counter++;
        }
        Assert.Equal(1, counter);
    }

    [Fact]
    public async Task CancelAsync_ShouldRemoveDraft()
    {
        var (sut, db, owner) = Create();
        var account = new Account(owner, AccountType.Giro, "A", null, Guid.NewGuid());
        db.Accounts.Add(account);
        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"{account.Iban}\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var draft in sut.CreateDraftAsync(owner, "z.csv", bytes, CancellationToken.None))
        {
            var ok = await sut.CancelAsync(draft.DraftId, owner, CancellationToken.None);
            Assert.True(ok);

            var fetched = await sut.GetDraftAsync(draft.DraftId, owner, CancellationToken.None);
            Assert.Null(fetched);
            counter++;
        }
        Assert.Equal(1, counter);
    }

    [Fact]
    public async Task CommitAsync_ShouldPersistImportAndEntries_AndMarkDraftCommitted()
    {
        var (sut, db, owner) = Create();
        var account = new Account(owner, AccountType.Giro, "Acc", null, Guid.NewGuid());
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var counter = 0;
        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"{account.Iban}\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        await foreach (var draft in sut.CreateDraftAsync(owner, "c.csv", bytes, CancellationToken.None))
        {
            var result = await sut.CommitAsync(draft.DraftId, owner, account.Id, ImportFormat.Csv, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(1, db.StatementImports.Count());
            Assert.Equal(draft.Entries.Count, db.StatementEntries.Count());
            var persistedDraft = await sut.GetDraftAsync(draft.DraftId, owner, CancellationToken.None);
            Assert.Equal(StatementDraftStatus.Committed, persistedDraft!.Status);
            counter++;
        }
        Assert.Equal(1, counter);
    }
}
