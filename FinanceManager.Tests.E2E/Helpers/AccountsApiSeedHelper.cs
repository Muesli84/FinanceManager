using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos.Postings;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// Creates accounts directly via the authenticated JSON API (through the browser session's cookies, see
/// <see cref="BrowserApiHelper"/>) instead of driving the account-creation UI. Tests use this to seed the
/// data they depend on quickly and reliably, keeping UI-driven flows in the test body focused on the
/// behavior actually under test rather than on account setup.
/// </summary>
public sealed class AccountsApiSeedHelper
{
    private readonly IPage _page;
    private readonly string? _databasePath;
    private readonly Guid? _ownerUserId;

    /// <summary>
    /// Creates the helper bound to an already-authenticated page whose session cookies will be used for
    /// the API calls.
    /// </summary>
    /// <param name="page">The Playwright page of the logged-in session to seed data for.</param>
    public AccountsApiSeedHelper(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Creates the helper with direct database seeding enabled for scenarios that need exact balances and postings.
    /// </summary>
    /// <param name="page">The Playwright page of the logged-in session to seed data for.</param>
    /// <param name="databasePath">Path to the SQLite database backing the E2E server.</param>
    /// <param name="ownerUserId">Owner user identifier for directly seeded accounts.</param>
    public AccountsApiSeedHelper(IPage page, string databasePath, Guid ownerUserId)
        : this(page)
    {
        _databasePath = databasePath;
        _ownerUserId = ownerUserId;
    }

    /// <summary>
    /// Creates a single Giro account with sensible defaults (a new "Test Bank" contact, security processing
    /// enabled, savings plan optional) via <c>POST /api/accounts</c>, so tests that only need "an account to
    /// exist" don't have to specify every field of <see cref="AccountCreateRequest"/> themselves.
    /// </summary>
    /// <param name="name">Display name for the account.</param>
    /// <param name="iban">IBAN to assign to the account.</param>
    /// <returns>The created account as returned by the API.</returns>
    public async Task<AccountDto> CreateAccountAsync(string name, string iban)
    {
        var request = new AccountCreateRequest(
            Name: name,
            Type: AccountType.Giro,
            Iban: iban,
            BankContactId: null,
            NewBankContactName: "Test Bank",
            SymbolAttachmentId: null,
            SavingsPlanExpectation: SavingsPlanExpectation.Optional,
            SecurityProcessingEnabled: true);

        return await BrowserApiHelper.PostJsonAsync<AccountCreateRequest, AccountDto>(_page, "/api/accounts", request);
    }

    /// <summary>
    /// Creates an account directly in the E2E database with a known balance and optional bank postings.
    /// </summary>
    /// <param name="name">Display name for the account.</param>
    /// <param name="iban">IBAN to assign to the account.</param>
    /// <param name="type">Account type to seed.</param>
    /// <param name="currentBalance">Current balance to expose in the account list and statistics.</param>
    /// <param name="bankContactName">Bank contact display name.</param>
    /// <param name="postings">Optional bank postings for period-to-date statistics.</param>
    /// <param name="bankContactId">Optional existing bank contact id to reuse instead of creating a new contact.</param>
    /// <returns>The identifiers of the seeded account and bank contact.</returns>
    public async Task<SeededAccount> CreateAccountWithBalanceAsync(
        string name,
        string iban,
        AccountType type,
        decimal currentBalance,
        string bankContactName,
        IReadOnlyList<SeededPosting>? postings = null,
        Guid? bankContactId = null)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !_ownerUserId.HasValue)
        {
            throw new InvalidOperationException("Direct account seeding requires database path and owner user id.");
        }

        await using var db = CreateContext(_databasePath);
        var resolvedBankContactId = bankContactId ?? Guid.Empty;
        if (!bankContactId.HasValue)
        {
            var bankContact = new Contact(_ownerUserId.Value, bankContactName, ContactType.Bank, null);
            resolvedBankContactId = bankContact.Id;
            db.Contacts.Add(bankContact);
        }

        var account = new FinanceManager.Domain.Accounts.Account(_ownerUserId.Value, type, name, iban, resolvedBankContactId);
        account.AdjustBalance(currentBalance);

        db.Accounts.Add(account);
        if (postings is { Count: > 0 })
        {
            db.Postings.AddRange(postings.Select(posting => new Posting(
                Guid.NewGuid(),
                PostingKind.Bank,
                account.Id,
                contactId: null,
                savingsPlanId: null,
                securityId: null,
                posting.BookingDate,
                posting.Amount)));
        }

        await db.SaveChangesAsync();
        return new SeededAccount(account.Id, resolvedBankContactId);
    }

    /// <summary>
    /// Creates a bank contact directly in the E2E database.
    /// </summary>
    public async Task<Guid> CreateBankContactAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !_ownerUserId.HasValue)
        {
            throw new InvalidOperationException("Direct contact seeding requires database path and owner user id.");
        }

        await using var db = CreateContext(_databasePath);
        var contact = new Contact(_ownerUserId.Value, name, ContactType.Bank, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact.Id;
    }

    /// <summary>
    /// Creates an account that references a missing bank contact for fallback-group scenarios.
    /// </summary>
    public async Task<SeededAccount> CreateAccountWithUnknownBankContactAsync(string name, string iban, AccountType type, decimal currentBalance)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !_ownerUserId.HasValue)
        {
            throw new InvalidOperationException("Direct account seeding requires database path and owner user id.");
        }

        await using var db = CreateContext(_databasePath);
        var missingContactId = Guid.NewGuid();
        var account = new FinanceManager.Domain.Accounts.Account(_ownerUserId.Value, type, name, iban, missingContactId);
        account.AdjustBalance(currentBalance);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return new SeededAccount(account.Id, missingContactId);
    }

    /// <summary>
    /// Data used to seed a bank posting for period-to-date E2E scenarios.
    /// </summary>
    /// <param name="BookingDate">Booking date used by account statistics.</param>
    /// <param name="Amount">Posting amount.</param>
    public sealed record SeededPosting(DateTime BookingDate, decimal Amount);

    /// <summary>
    /// Identifiers returned from direct account seeding.
    /// </summary>
    /// <param name="AccountId">Seeded account identifier.</param>
    /// <param name="BankContactId">Seeded bank contact identifier.</param>
    public sealed record SeededAccount(Guid AccountId, Guid BankContactId);

    private static AppDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new AppDbContext(options);
    }
}
