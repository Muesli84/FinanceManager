using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftSecurityClassificationTests
{
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

        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, conn, ownerUser.Id);
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

    [Fact]
    public async Task AutoAssignSecurity_ByIdentifier_SetsSecurityId()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange securities
        var sec = new Security(owner, name: "ETF World", identifier: "DE000A0XYZ", description: null, alphaVantageCode: "WLD.F", currencyCode: "EUR", categoryId: null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Create empty draft
        var draft = await CreateDraftAsync(db, owner);

        // Act: add entry whose subject contains the identifier -> triggers classification
        var added = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 100m, "Trade DE000A0XYZ", CancellationToken.None);

        // Assert
        Assert.NotNull(added);
        var e = added!.Entries.Single();
        Assert.Equal(sec.Id, e.SecurityId);

        conn.Dispose();
    }

    [Fact]
    public async Task AutoAssignSecurity_ByNameWithUmlauts_SetsSecurityId()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange: security name with umlauts; classification normalizes to plain ASCII (ue/oe/ae/ss)
        var sec = new Security(owner, name: "Münchener Rückversicherung", identifier: "DE000MNRK", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner);

        // Subject uses ue/ue instead of umlauts and includes spaces/punctuation
        var dto = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 12.34m, "Dividende Muenchener Rueckversicherung AG", CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(sec.Id, dto!.Entries.Single().SecurityId);

        conn.Dispose();
    }

    [Fact]
    public async Task AutoAssignSecurity_MultipleMatches_AssignsFirstByName_AndKeepsStatusOpen()
    {
        var (sut, db, conn, owner) = Create();

        // Arrange two matching securities (order by Name ascending used in classification)
        var first = new Security(owner, name: "AAA Corp", identifier: "DE111111", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        var second = new Security(owner, name: "ZZZ Corp", identifier: "DE222222", description: null, alphaVantageCode: null, currencyCode: "EUR", categoryId: null);
        db.Securities.AddRange(first, second);
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner);

        // Subject contains both identifiers -> ambiguous match
        var dto = await sut.AddEntryAsync(draft.Id, owner, DateTime.Today, 50m, "Trade DE111111 + DE222222", CancellationToken.None);

        Assert.NotNull(dto);
        var entry = dto!.Entries.Single();
        Assert.Equal(first.Id, entry.SecurityId); // assigned the first by name (AAA Corp)
        Assert.Equal(StatementDraftEntryStatus.Open, entry.Status); // remains open on ambiguity

        conn.Dispose();
    }
}
