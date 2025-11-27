using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.Web
{
    public sealed class PostingsQueryServiceTests
    {
        private static (PostingsQueryService svc, AppDbContext db, Guid owner) Create()
        {
            var conn = new SqliteConnection("DataSource=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
            var db = new AppDbContext(options);
            db.Database.EnsureCreated();
            var owner = new FinanceManager.Domain.Users.User("owner", "hash", true);
            db.Users.Add(owner);
            db.SaveChanges();
            var self = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
            db.Contacts.Add(self);
            db.SaveChanges();
            var svc = new PostingsQueryService(db);
            return (svc, db, owner.Id);
        }

        [Fact]
        public async Task GetContactPostings_TwoLinkedPostingsOnDifferentAccounts_ReturnsBothWithLinkedInfo()
        {
            var (svc, db, owner) = Create();

            // create bank contacts with symbols
            var bankContact1 = new Contact(owner, "Bank A", ContactType.Bank, null, null);
            var bankContact2 = new Contact(owner, "Bank B", ContactType.Bank, null, null);
            var sym1 = Guid.NewGuid();
            var sym2 = Guid.NewGuid();
            bankContact1.SetSymbolAttachment(sym1);
            bankContact2.SetSymbolAttachment(sym2);
            db.Contacts.AddRange(bankContact1, bankContact2);
            await db.SaveChangesAsync();

            // create accounts referencing bank contacts (no account symbol set -> fallback to contact symbol)
            var acc1 = new Account(owner, AccountType.Giro, "G1", null, bankContact1.Id);
            var acc2 = new Account(owner, AccountType.Savings, "S1", null, bankContact2.Id);
            db.Accounts.AddRange(acc1, acc2);
            await db.SaveChangesAsync();

            var contact = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

            var date = DateTime.UtcNow.Date;

            // First transfer: Giro -> Savings (bank: acc1, contact posting amount -100)
            var gid1 = Guid.NewGuid();
            var bank1 = new Posting(Guid.NewGuid(), PostingKind.Bank, acc1.Id, null, null, null, date, date, -100m, "", null, null, null, null).SetGroup(gid1);
            var contact1 = new Posting(Guid.NewGuid(), PostingKind.Contact, null, contact.Id, null, null, date, date, -100m, "T1", null, null, null, null).SetGroup(gid1);

            // Second transfer: Giro -> Savings (bank: acc2, contact posting amount +100)
            var gid2 = Guid.NewGuid();
            var bank2 = new Posting(Guid.NewGuid(), PostingKind.Bank, acc2.Id, null, null, null, date, date, 100m, "", null, null, null, null).SetGroup(gid2);
            var contact2 = new Posting(Guid.NewGuid(), PostingKind.Contact, null, contact.Id, null, null, date, date, 100m, "T2", null, null, null, null).SetGroup(gid2);

            // link contact postings to each other
            contact1.SetLinkedPosting(contact2.Id);
            contact2.SetLinkedPosting(contact1.Id);

            db.Postings.AddRange(bank1, contact1, bank2, contact2);
            await db.SaveChangesAsync();

            var res = await svc.GetContactPostingsAsync(contact.Id, 0, 50, null, null, null, owner, default);
            Assert.NotNull(res);
            Assert.Equal(2, res.Count);

            var r1 = res.FirstOrDefault(x => x.Id == contact1.Id);
            var r2 = res.FirstOrDefault(x => x.Id == contact2.Id);
            Assert.NotNull(r1);
            Assert.NotNull(r2);
            Assert.NotNull(r1.LinkedPostingId);
            Assert.NotNull(r2.LinkedPostingId);
            Assert.Equal(r1.LinkedPostingId, r2.Id);
            Assert.Equal(r2.LinkedPostingId, r1.Id);

            // Ensure bank posting symbols are returned (from bank contact fallback)
            Assert.Equal(sym1, r1.BankPostingAccountSymbolAttachmentId);
            Assert.Equal(sym2, r2.BankPostingAccountSymbolAttachmentId);

            // Ensure linked posting's account symbol (or its bank contact symbol) is returned
            // r1 is linked to contact2 -> its linked account symbol should be sym2
            Assert.Equal(sym2, r1.LinkedPostingAccountSymbolAttachmentId);
            // r2 is linked to contact1 -> its linked account symbol should be sym1
            Assert.Equal(sym1, r2.LinkedPostingAccountSymbolAttachmentId);
        }
    }
}
