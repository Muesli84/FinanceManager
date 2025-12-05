using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinanceManager.Tests.Infrastructure;

public sealed class ContactServiceAliasTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(opts);
    }

    private static async Task<(ContactService svc, AppDbContext db, Guid userId, Guid contactId)> SeedAsync()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        var c = new FinanceManager.Domain.Contacts.Contact(userId, "Source", ContactType.Person, null, null, false);
        db.Contacts.Add(c);
        await db.SaveChangesAsync();
        return (new ContactService(db), db, userId, c.Id);
    }

    [Fact]
    public async Task AddAliasAsync_ShouldPreventDuplicate_ForSameContact_CaseInsensitive()
    {
        var (svc, db, user, contact) = await SeedAsync();

        await svc.AddAliasAsync(contact, user, "Foo", CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(async () => await svc.AddAliasAsync(contact, user, "foo", CancellationToken.None));
    }

    [Fact]
    public async Task MergeAsync_ShouldNotCreateDuplicateAliases_AndReassign()
    {
        var db = CreateDb();
        var user = Guid.NewGuid();
        var source = new FinanceManager.Domain.Contacts.Contact(user, "SourceName", ContactType.Person, null, null, false);
        var target = new FinanceManager.Domain.Contacts.Contact(user, "TargetName", ContactType.Person, null, null, false);
        db.Contacts.AddRange(source, target);
        await db.SaveChangesAsync();

        // target has alias "x"; source has alias "X" (case-insensitive duplicate)
        db.AliasNames.Add(new FinanceManager.Domain.Contacts.AliasName(target.Id, "x"));
        db.AliasNames.Add(new FinanceManager.Domain.Contacts.AliasName(source.Id, "X"));
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.MergeAsync(user, source.Id, target.Id, CancellationToken.None);

        Assert.Equal(target.Id, result.Id);

        var aliases = await db.AliasNames.AsNoTracking().Where(a => a.ContactId == target.Id).ToListAsync();
        // Assert: exactly one alias with value "x" ignoring case remains
        Assert.Equal(1, aliases.Select(a => a.Pattern.ToLowerInvariant()).Count(p => p == "x"));
        // And no case-insensitive duplicates overall
        var distinct = aliases.Select(a => a.Pattern.ToLowerInvariant()).Distinct().Count();
        Assert.Equal(distinct, aliases.Count);
        // Source contact removed
        Assert.Null(await db.Contacts.FindAsync(source.Id));
    }
}
