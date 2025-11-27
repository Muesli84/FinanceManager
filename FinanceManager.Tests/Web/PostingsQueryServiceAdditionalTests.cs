using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Infrastructure;
using FinanceManager.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.Web
{
    public sealed class PostingsQueryServiceAdditionalTests
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
        public async Task GetAccountPostingsAsync_ReturnsBankPostingWithAccountSymbol_WhenAccountHasSymbol()
        {
            var (svc, db, owner) = Create();

            var bankContact = new Contact(owner, "Bank", ContactType.Bank, null, null);
            db.Contacts.Add(bankContact);
            await db.SaveChangesAsync();

            var acc = new Account(owner, AccountType.Giro, "A1", null, bankContact.Id);
            var sym = Guid.NewGuid();
            acc.SetSymbolAttachment(sym);
            db.Accounts.Add(acc);
            await db.SaveChangesAsync();

            var date = DateTime.UtcNow.Date;
            var bankPost = new Posting(Guid.NewGuid(), PostingKind.Bank, acc.Id, null, null, null, date, date, 42m, "", null, null, null, null).SetGroup(Guid.NewGuid());
            db.Postings.Add(bankPost);
            await db.SaveChangesAsync();

            var res = await svc.GetAccountPostingsAsync(acc.Id, 0, 50, null, null, null, owner);
            Assert.NotNull(res);
            Assert.Single(res);
            var dto = res[0];
            Assert.Equal(PostingKind.Bank, dto.Kind);
            Assert.Equal(acc.Id, dto.BankPostingAccountId);
            Assert.Equal(sym, dto.BankPostingAccountSymbolAttachmentId);
        }

        [Fact]
        public async Task GetSavingsPlanPostingsAsync_ReturnsSavingsPlanPostings_WhenOwned()
        {
            var (svc, db, owner) = Create();

            var plan = new SavingsPlan(owner, "PlanX", SavingsPlanType.OneTime, null, null, null);
            db.SavingsPlans.Add(plan);
            await db.SaveChangesAsync();

            var date = DateTime.UtcNow.Date;
            var p = new Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, date, date, 10m, "S", null, null, null, null).SetGroup(Guid.NewGuid());
            db.Postings.Add(p);
            await db.SaveChangesAsync();

            var res = await svc.GetSavingsPlanPostingsAsync(plan.Id, 0, 50, null, null, null, owner);
            Assert.NotNull(res);
            Assert.Single(res);
            Assert.Equal(PostingKind.SavingsPlan, res[0].Kind);
            Assert.Equal(plan.Id, res[0].SavingsPlanId);
        }

        [Fact]
        public async Task GetSecurityPostingsAsync_ReturnsSecurityPostings_WhenOwned()
        {
            var (svc, db, owner) = Create();

            var sec = new Security(owner, "SEC", "ID", null, null, "EUR", null);
            db.Securities.Add(sec);
            await db.SaveChangesAsync();

            var date = DateTime.UtcNow.Date;
            var p = new Posting(Guid.NewGuid(), PostingKind.Security, null, null, null, sec.Id, date, date, 5m, "X", null, null, null, 1m).SetGroup(Guid.NewGuid());
            db.Postings.Add(p);
            await db.SaveChangesAsync();

            var res = await svc.GetSecurityPostingsAsync(sec.Id, 0, 50, null, null, owner);
            Assert.NotNull(res);
            Assert.Single(res);
            Assert.Equal(PostingKind.Security, res[0].Kind);
            Assert.Equal(sec.Id, res[0].SecurityId);
        }

        [Fact]
        public async Task GetAccountPostingsAsync_FallsBackToBankContactSymbol_WhenAccountHasNoSymbol()
        {
            var (svc, db, owner) = Create();

            var bankContact = new Contact(owner, "BankContact", ContactType.Bank, null, null);
            var bankSym = Guid.NewGuid();
            bankContact.SetSymbolAttachment(bankSym);
            db.Contacts.Add(bankContact);
            await db.SaveChangesAsync();

            var acc = new Account(owner, AccountType.Giro, "A-NoSym", null, bankContact.Id);
            // no account symbol set
            db.Accounts.Add(acc);
            await db.SaveChangesAsync();

            var date = DateTime.UtcNow.Date;
            var bankPost = new Posting(Guid.NewGuid(), PostingKind.Bank, acc.Id, null, null, null, date, date, 1m, "", null, null, null, null).SetGroup(Guid.NewGuid());
            db.Postings.Add(bankPost);
            await db.SaveChangesAsync();

            var res = await svc.GetAccountPostingsAsync(acc.Id, 0, 50, null, null, null, owner);
            Assert.Single(res);
            Assert.Equal(bankSym, res[0].BankPostingAccountSymbolAttachmentId);
        }

        [Fact]
        public async Task GetContactPostingsAsync_LinkedPostingSymbolFallbacksToLinkedAccountContactSymbol()
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

            var res = await svc.GetContactPostingsAsync(contact.Id, 0, 50, null, null, null, owner);
            Assert.NotNull(res);
            Assert.Equal(2, res.Count);

            var r1 = res.FirstOrDefault(x => x.Id == contact1.Id);
            var r2 = res.FirstOrDefault(x => x.Id == contact2.Id);
            Assert.NotNull(r1);
            Assert.NotNull(r2);

            // bank posting symbols fallback from bank contact
            Assert.Equal(sym1, r1.BankPostingAccountSymbolAttachmentId);
            Assert.Equal(sym2, r2.BankPostingAccountSymbolAttachmentId);

            // linked posting account symbol should reference the opposite symbol
            Assert.Equal(sym2, r1.LinkedPostingAccountSymbolAttachmentId);
            Assert.Equal(sym1, r2.LinkedPostingAccountSymbolAttachmentId);
        }

        [Fact]
        public async Task GetContactPostingsAsync_LinkedPostingWithoutBankPosting_YieldsNullLinkedSymbol()
        {
            var (svc, db, owner) = Create();

            var contact = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
            var date = DateTime.UtcNow.Date;

            // linked postings but no bank postings for the linked group
            var gid = Guid.NewGuid();
            var contactA = new Posting(Guid.NewGuid(), PostingKind.Contact, null, contact.Id, null, null, date, date, -10m, "A", null, null, null, null).SetGroup(gid);
            var contactB = new Posting(Guid.NewGuid(), PostingKind.Contact, null, contact.Id, null, null, date, date, 10m, "B", null, null, null, null).SetGroup(gid);
            contactA.SetLinkedPosting(contactB.Id);
            contactB.SetLinkedPosting(contactA.Id);
            db.Postings.AddRange(contactA, contactB);
            await db.SaveChangesAsync();

            var res = await svc.GetContactPostingsAsync(contact.Id, 0, 50, null, null, null, owner);
            Assert.NotNull(res);
            Assert.Equal(2, res.Count);

            var ra = res.First(x => x.Id == contactA.Id);
            var rb = res.First(x => x.Id == contactB.Id);
            Assert.Null(ra.LinkedPostingAccountSymbolAttachmentId);
            Assert.Null(rb.LinkedPostingAccountSymbolAttachmentId);
        }
    }
}
