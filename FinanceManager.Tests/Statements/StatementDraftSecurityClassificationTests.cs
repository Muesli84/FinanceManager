using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Application.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceManager.Tests.TestHelpers;

namespace FinanceManager.Tests.Statements;

/// <summary>
/// Covers auto-assignment of a <see cref="Security"/> to a statement entry during classification: matching by
/// identifier or by name (including umlaut-normalized name matching), and the ambiguous-match fallback when
/// several securities could apply. Auto-assignment only ever triggers for a detected account that has security
/// processing enabled.
/// </summary>
public sealed class StatementDraftSecurityClassificationTests
{
    // Security classification never needs to load statement files (that happens during upload/create-draft),
    // but the constructor parameter is non-nullable, so a trivial stub stands in for the real factory.
    private sealed class StubStatementFileFactory : IStatementFileFactory
    {
        public IStatementFile? Load(string fileName, byte[] fileBytes) => null;
    }

    private static (StatementDraftService sut, AppDbContext db, SqliteConnection conn, Guid owner) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var ownerUser = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(ownerUser);
        db.SaveChanges();
        // ensure self contact exists (required by classification logic)
        var self = new Contact(ownerUser.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        db.SaveChanges();

        var accountService = new StubAccountService();
        var sut = new StatementDraftService(db, new PostingAggregateService(db), accountService, new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null);
        return (sut, db, conn, ownerUser.Id);
    }

    /// <summary>
    /// Creates a bank contact and an account with security processing enabled.
    /// Securities may only be auto-assigned when a detected account explicitly allows it.
    /// </summary>
    /// <param name="db">The db.</param>
    /// <param name="owner">The owner.</param>
    /// <returns>The result.</returns>
    private static async Task<Account> CreateSecurityAccountAsync(AppDbContext db, Guid owner)
    {
        var bank = new Contact(owner, "Testbank", ContactType.Bank, null, null);
        db.Contacts.Add(bank);
        await db.SaveChangesAsync();

        var account = new Account(owner, AccountType.Giro, "Depot", null, bank.Id);
        // SecurityProcessingEnabled defaults to true; set explicitly for clarity
        account.SetSecurityProcessingEnabled(true);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return account;
    }


    private static async Task<StatementDraft> CreateDraftAsync(AppDbContext db, Guid owner, Guid? accountId = null)
    {
        var draft = new StatementDraft(owner, "file.csv", "", "");
        if (accountId != null)
        {
            draft.SetDetectedAccount(accountId.Value);
        }
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    /// <summary>
    /// Verifies that an entry whose subject contains the security identifier gets the security auto-assigned.
    /// A detected account with security processing enabled is required for auto-assignment.
    /// </summary>
    [Fact]
    public async Task AutoAssignSecurity_ByIdentifier_SetsSecurityId()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange: bank account with security processing enabled
        var account = await CreateSecurityAccountAsync(db, owner);

        // Arrange securities
        var sec = new Security(owner, name: "ETF World", identifier: "DE000A0XYZ", description: null, alphaVantageCode: "WLD.F", currencyCode: "EUR", categoryId: null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Create draft linked to the account
        var draft = await CreateDraftAsync(db, owner, accountId: account.Id);

        // Act: add entry whose subject contains the identifier -> triggers classification
        var added = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 100m, "Trade DE000A0XYZ", CancellationToken.None);

        // Assert
        Assert.NotNull(added);
        var e = added!.Entries.Single();
        Assert.Equal(sec.Id, e.SecurityId);

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that a security name containing umlauts is matched when the entry subject uses the
    /// ASCII equivalents (ue/oe/ae). A detected account with security processing enabled is required.
    /// </summary>
    [Fact]
    public async Task AutoAssignSecurity_ByNameWithUmlauts_SetsSecurityId()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange: bank account with security processing enabled
        var account = await CreateSecurityAccountAsync(db, owner);

        // Arrange: security name with umlauts; classification normalizes to plain ASCII (ue/oe/ae/ss)
        var sec = new Security(owner, name: "Münchener Rückversicherung", identifier: "DE000MNRK", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var draft = await CreateDraftAsync(db, owner, accountId: account.Id);

        // Subject uses ue/ue instead of umlauts and includes spaces/punctuation
        var dto = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 12.34m, "Dividende Muenchener Rueckversicherung AG", CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(sec.Id, dto!.Entries.Single().SecurityId);

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that when multiple securities match the entry subject, the first by name is assigned
    /// and the entry status remains Open to flag the ambiguity.
    /// A detected account with security processing enabled is required.
    /// </summary>
    [Fact]
    public async Task AutoAssignSecurity_MultipleMatches_AssignsFirstByName_AndKeepsStatusOpen()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange: bank account with security processing enabled
        var account = await CreateSecurityAccountAsync(db, owner);

        // Arrange two matching securities (order by Name ascending used in classification)
        var first = new Security(owner, name: "AAA Corp", identifier: "DE111111", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        var second = new Security(owner, name: "ZZZ Corp", identifier: "DE222222", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        db.Securities.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var draft = await CreateDraftAsync(db, owner, accountId: account.Id);

        // Subject contains both identifiers -> ambiguous match
        var dto = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 50m, "Trade DE111111 + DE222222", CancellationToken.None);

        Assert.NotNull(dto);
        var entry = dto!.Entries.Single();
        Assert.Equal(first.Id, entry.SecurityId); // assigned the first by name (AAA Corp)
        Assert.Equal(StatementDraftEntryStatus.Open, entry.Status); // remains open on ambiguity

        conn.Dispose();
    }
}
