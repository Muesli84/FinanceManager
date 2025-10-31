using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain.Users;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ContactType = FinanceManager.Shared.Dtos.ContactType;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftSplitLinkTests
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
        var owner = new User("owner", "hash", true);
        db.Users.Add(owner);
        db.SaveChanges();
        var self = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        db.SaveChanges();
        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, conn, owner.Id);
    }

    [Fact]
    public async Task SetEntrySplitDraftAsync_ShouldLink_WhenUploadGroupIdDiffers()
    {
        var (sut, db, conn, owner) = Create();

        var parent = new StatementDraft(owner, "parent.pdf", null, null);
        parent.SetUploadGroup(Guid.NewGuid());
        db.StatementDrafts.Add(parent);
        var child = new StatementDraft(owner, "child.pdf", null, null);
        child.SetUploadGroup(Guid.NewGuid()); // different group -> should be allowed now
        db.StatementDrafts.Add(child);
        await db.SaveChangesAsync();

        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, isPaymentIntermediary: true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SetEntrySplitDraftAsync(parent.Id, pEntry.Id, child.Id, owner, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var updated = await db.StatementDraftEntries.FirstAsync(e => e.Id == pEntry.Id);
        Assert.Equal(child.Id, updated.SplitDraftId);

        conn.Dispose();
    }

    [Fact]
    public async Task SetEntrySplitDraftAsync_ShouldThrow_WhenUploadGroupIdSame()
    {
        var (sut, db, conn, owner) = Create();

        var uploadId = Guid.NewGuid();
        var parent = new StatementDraft(owner, "parent.pdf", null, null);
        parent.SetUploadGroup(uploadId);
        db.StatementDrafts.Add(parent);
        var child = new StatementDraft(owner, "child.pdf", null, null);
        child.SetUploadGroup(uploadId); // same group -> should now fail
        db.StatementDrafts.Add(child);
        await db.SaveChangesAsync();

        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, isPaymentIntermediary: true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var act = async () => await sut.SetEntrySplitDraftAsync(parent.Id, pEntry.Id, child.Id, owner, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("must NOT originate from the same upload", ex.Message, StringComparison.OrdinalIgnoreCase);

        conn.Dispose();
    }
}
