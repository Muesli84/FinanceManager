using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class StatementDraftClassificationTests
{
    private static (StatementDraftService sut, AppDbContext db, SqliteConnection conn, Guid ownerId) Create()
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

        var sut = new StatementDraftService(db);
        return (sut, db, conn, owner.Id);
    }

    private static async Task<Account> AddBankAccountAsync(AppDbContext db, Contact? bankContact = null)
    {
        var owner = db.Users.First().Id;
        var accountName = $"Konto {db.Accounts.Count() + 1}";
        var iban = $"DE123{db.Accounts.Count() + 1}";
        if (bankContact is null)
        {
            bankContact = new Contact(owner, $"Bank {db.Contacts.Count(c => c.Type == ContactType.Bank) + 1}", ContactType.Bank, null, null);
            db.Contacts.Add(bankContact);
            await db.SaveChangesAsync();
        }
        var account = new Account(owner, AccountType.Giro, accountName, iban, bankContact.Id);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }
    private static async Task<StatementDraft> CreateStatementDraftAsync(AppDbContext db, Account account, Action<StatementDraft> callback)
    {
        var owner = db.Users.First().Id;
        var draft = new StatementDraft(owner, "file.csv");
        draft.AccountName = account.Iban;
        callback(draft);
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    private static async Task<Contact> AddContact(AppDbContext db, Guid owner, string name, ContactType contactType = ContactType.Person, Guid? categoryId = null, string? description = null, bool? isPaymentIntermediary = null, params string[] aliasNames)
    {
        var contact = new Contact(owner, name, contactType, categoryId, description, isPaymentIntermediary);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        foreach (var alias in aliasNames)
        {
            db.AliasNames.Add(new AliasName(contact.Id, alias));
        }
        await db.SaveChangesAsync();
        return contact;
    }


    [Fact]
    public async Task BankAccount_IsRecognized()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        draft.DetectedAccountId.Should().Be(account.Id);
        conn.Dispose();
    }

    
    [Fact]
    public async Task BankAccount_IsRecognized_ForSingleAccount()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AccountName = "";
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        draft.DetectedAccountId.Should().Be(account.Id);
        conn.Dispose();
    }

    

    [Fact]
    public async Task BankAccount_IsNotRecognized()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = await db.Contacts.FirstAsync(c => c.Id == account.BankContactId);
        account = await AddBankAccountAsync(db, contact);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AccountName = "DE456";
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", false);
        });
        
        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        draft.DetectedAccountId.Should().BeNull();
        conn.Dispose();
    }

    [Fact]
    public async Task BankAccount_IsNotRecognized_ForSingleAccount()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AccountName = "DE456";
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        draft.DetectedAccountId.Should().BeNull();
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_IsAnnounced_StatusSet()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", true);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.Status.Should().Be(StatementDraftEntryStatus.Announced);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_AlreadyBooked_IsIgnored()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", "Empfänger", DateTime.Today, "EUR", "Buchung", false);
        });
        db.StatementEntries.Add(new StatementEntry(Guid.NewGuid(), DateTime.Today, 100, "Test", "hash", "Empfänger", DateTime.Today, "EUR", "Buchung", false));
        await db.SaveChangesAsync();

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.Status.Should().Be(StatementDraftEntryStatus.AlreadyBooked);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_ContactAssigned_NotBankOrSelf()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = await AddContact(db, owner, "Max Mustermann");
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", contact.Name, DateTime.Today, "EUR", "Buchung", false);
        });
        
        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(contact.Id);
        conn.Dispose();
    }

    

    [Fact]
    public async Task Entry_BankContact_MatchesAccountBankContact()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = db.Contacts.First(c => c.Id == account.BankContactId);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", contact.Name, DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(contact.Id);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_NoRecipientName_BankContactAssigned_IfRecognized()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = db.Contacts.First(c => c.Id == account.BankContactId);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", "", DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(contact.Id);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_BankContact_NotMatchingAccountBankContact_SelfContactAssigned()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = db.Contacts.First(c => c.Id == account.BankContactId);
        var otherContact = await AddContact(db, owner, "Andere Bank", ContactType.Bank);
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Test", otherContact.Name, DateTime.Today, "EUR", "Buchung", false);
        });
        
        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(owner);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_PaymentIntermediary_ContactFoundBySubject()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = db.Contacts.First(c => c.Id == account.BankContactId);
        var intermediary = await AddContact(db, owner, "PayPal", ContactType.Organization, null, null, true);
        var recipient = await AddContact(db, owner, "Max Mustermann", ContactType.Person, null, null, false, "Rechnung*");
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Rechnung 123", "PayPal", DateTime.Today, "EUR", "Buchung", false);
        });
        
        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(recipient.Id);
        conn.Dispose();
    }

    [Fact]
    public async Task Entry_PaymentIntermediary_ContactNotFoundBySubject()
    {
        var (sut, db, conn, owner) = Create();
        var account = await AddBankAccountAsync(db);
        var contact = db.Contacts.First(c => c.Id == account.BankContactId);
        var intermediary = await AddContact(db, owner, "PayPal", ContactType.Organization, null, null, true);
        var recipient = await AddContact(db, owner, "Max Mustermann", ContactType.Person, null, null, false, "Rechnung*");
        var draft = await CreateStatementDraftAsync(db, account, (draft) =>
        {
            draft.AddEntry(DateTime.Today, 100, "Unbekannt", "PayPal", DateTime.Today, "EUR", "Buchung", false);
        });

        await sut.ClassifyAsync(draft.Id, owner, CancellationToken.None);

        var entry = draft.Entries.First();
        entry.ContactId.Should().Be(intermediary.Id);
        conn.Dispose();
    }
}