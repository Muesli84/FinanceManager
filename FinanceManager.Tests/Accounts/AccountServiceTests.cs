using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Application.Accounts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Accounts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManager.Tests.Accounts;

/// <summary>
/// Covers <see cref="AccountService"/>'s core account lifecycle: creating accounts with per-user IBAN
/// uniqueness enforcement, and deleting accounts while correctly cascading the ownership of their
/// associated bank contact (removing the bank contact only when no other account still references it).
/// </summary>
public sealed class AccountServiceTests
{
    private sealed class FixedPeriodProvider : IAccountStatisticsPeriodProvider
    {
        public Task<AccountStatisticsPeriod> GetPeriodAsync(Guid ownerUserId, CancellationToken ct)
            => Task.FromResult(new AccountStatisticsPeriod(
                new DateTime(2026, 9, 5),
                new DateTime(2026, 1, 1),
                new DateTime(2026, 9, 1),
                new DateTime(2026, 9, 6)));
    }

    private static (AccountService sut, AppDbContext db) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var sut = new AccountService(db, new FixedPeriodProvider());
        return (sut, db);
    }

    private static (AccountService sut, AppDbContext db, SqliteConnection connection) CreateSqlite()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return (new AccountService(db, new FixedPeriodProvider()), db, connection);
    }

    /// <summary>
    /// Verifies that a valid account with a unique IBAN is persisted and the returned DTO reflects the
    /// values passed to <see cref="AccountService.CreateAsync"/>.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ShouldCreate_WhenValidAndUniqueIbanPerUser()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank A", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = await sut.CreateAsync(owner, "Konto 1", AccountType.Giro, "DE123", bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);

        Assert.Equal("Konto 1", dto.Name);
        Assert.Equal("DE123", dto.Iban);
        Assert.Equal(1, db.Accounts.Count());
    }

    /// <summary>
    /// Ensures IBAN uniqueness is enforced per user: creating a second account with an IBAN already used
    /// by the same owner is rejected with an <see cref="ArgumentException"/> that names the IBAN as the
    /// cause, preventing silent duplicate-account creation for the same bank account.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ShouldFail_WhenDuplicateIbanForSameUser()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank A", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await sut.CreateAsync(owner, "A", AccountType.Giro, "DE999", bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);
        Func<Task> act = () => sut.CreateAsync(owner, "B", AccountType.Giro, "DE999", bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ArgumentException>(act);
        Assert.Contains("IBAN", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Confirms that deleting the only account linked to a bank contact also removes that bank contact,
    /// so orphaned bank contacts do not accumulate once their last referencing account is gone.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ShouldDeleteBankContact_WhenLastAccountOfContact()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank B", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var acc = await sut.CreateAsync(owner, "Main", AccountType.Giro, null, bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);

        var ok = await sut.DeleteAsync(acc.Id, owner, CancellationToken.None);

        Assert.True(ok);
        Assert.False(db.Accounts.Any());
        Assert.False(db.Contacts.Any(c => c.Id == bankContact.Id));
    }

    /// <summary>
    /// Guards against over-eager cleanup: deleting one account must not delete a bank contact that is
    /// still referenced by another account of the same owner.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ShouldNotDeleteBankContact_WhenOtherAccountsExist()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank C", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var a1 = await sut.CreateAsync(owner, "A1", AccountType.Giro, null, bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);
        var a2 = await sut.CreateAsync(owner, "A2", AccountType.Giro, null, bankContact.Id, SavingsPlanExpectation.Optional, true, false, CancellationToken.None);

        var ok = await sut.DeleteAsync(a1.Id, owner, CancellationToken.None);

        Assert.True(ok);
        Assert.True(db.Contacts.Any(c => c.Id == bankContact.Id));
    }

    /// <summary>
    /// Verifies inclusive period starts and exclusive tomorrow boundary for year-to-date and month-to-date statistics.
    /// </summary>
    [Fact]
    public async Task GetStatistics_BookingDateBoundaries_AreInclusiveStartExclusiveTomorrow()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
        var account = new Account(owner, AccountType.Giro, "Main", "DE11", bankContact.Id);
        db.Contacts.Add(bankContact);
        db.Accounts.Add(account);
        db.Postings.AddRange(
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2025, 12, 31, 23, 59, 59), 999m),
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 1, 1), 10m),
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 8, 31, 23, 59, 59), 20m),
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 1), 30m),
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 5, 23, 59, 59), 40m),
            new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 6), 888m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var statistics = await sut.GetStatisticsAsync(owner, q: null, TestContext.Current.CancellationToken);

        Assert.Equal(100m, statistics.YearToDateChange);
        Assert.Equal(70m, statistics.MonthToDateChange);
    }

    /// <summary>
    /// Covers the account search contract for name, normalized IBAN, literal wildcard characters and length validation.
    /// </summary>
    [Fact]
    public async Task AccountQuery_SearchesNameAndNormalizedIbanCaseInsensitivelyAndRejectsOverlength()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        db.Accounts.AddRange(
            new Account(owner, AccountType.Giro, "Household", "DE12 3456-7890", bankContact.Id),
            new Account(owner, AccountType.Giro, "Wildcard %_[*?]", "DE99", bankContact.Id),
            new Account(Guid.NewGuid(), AccountType.Giro, "Household foreign", "DE1234567890", bankContact.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var byName = await sut.ListAsync(owner, 0, 50, null, "house", TestContext.Current.CancellationToken);
        var byIban = await sut.ListAsync(owner, 0, 50, null, "de12\u00A03456 7890", TestContext.Current.CancellationToken);
        var byLiteralWildcard = await sut.ListAsync(owner, 0, 50, null, "%_[*?]", TestContext.Current.CancellationToken);

        Assert.Single(byName);
        Assert.Single(byIban);
        Assert.Single(byLiteralWildcard);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.ListAsync(owner, 0, 50, null, new string('x', 201), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that non-ASCII case-insensitive name search is stable on SQLite and shared by list and statistics.
    /// </summary>
    [Fact]
    public async Task AccountQuery_SearchesNonAsciiNameCaseInsensitively_OnSqlite()
    {
        var (sut, db, connection) = CreateSqlite();
        await using (db)
        await using (connection)
        {
            var owner = Guid.NewGuid();
            var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
            var account = new Account(owner, AccountType.Giro, "MÄDCHEN Rücklage", "DE77", bankContact.Id);
            account.AdjustBalance(42m);
            db.Contacts.Add(bankContact);
            db.Accounts.Add(account);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var list = await sut.ListAsync(owner, 0, 50, null, "mädchen", TestContext.Current.CancellationToken);
            var statistics = await sut.GetStatisticsAsync(owner, "mädchen", TestContext.Current.CancellationToken);

            Assert.Single(list);
            Assert.Equal(1, statistics.AccountCount);
            Assert.Equal(42m, statistics.TotalBalance);
        }
    }

    /// <summary>
    /// Verifies that null, empty and whitespace-only searches all disable filtering.
    /// </summary>
    [Fact]
    public async Task AccountQuery_NullWhitespaceAndClearedSearchDisableFilter()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        db.Accounts.AddRange(
            new Account(owner, AccountType.Giro, "Alpha", "DE11", bankContact.Id),
            new Account(owner, AccountType.Giro, "Beta", "DE22", bankContact.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var nullSearch = await sut.ListAsync(owner, 0, 50, null, null, TestContext.Current.CancellationToken);
        var emptySearch = await sut.ListAsync(owner, 0, 50, null, string.Empty, TestContext.Current.CancellationToken);
        var whitespaceSearch = await sut.ListAsync(owner, 0, 50, null, "   ", TestContext.Current.CancellationToken);

        Assert.Equal(2, nullSearch.Count);
        Assert.Equal(nullSearch.Count, emptySearch.Count);
        Assert.Equal(nullSearch.Count, whitespaceSearch.Count);
    }

    /// <summary>
    /// Verifies that list and statistics share the same server-side account search before sorting and paging.
    /// </summary>
    [Fact]
    public async Task AccountQuery_SearchScopeIsSharedBeforeSortingPagingAndStatistics()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
        db.Contacts.Add(bankContact);

        var firstMatch = new Account(owner, AccountType.Giro, "ZZZ Needle", "DE11", bankContact.Id);
        firstMatch.AdjustBalance(10m);
        var secondMatch = new Account(owner, AccountType.Savings, "AAA Needle", "DE22", bankContact.Id);
        secondMatch.AdjustBalance(20m);
        var nonMatch = new Account(owner, AccountType.Giro, "Middle", "DE33", bankContact.Id);
        nonMatch.AdjustBalance(999m);
        db.Accounts.AddRange(firstMatch, secondMatch, nonMatch);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstPage = await sut.ListAsync(owner, skip: 0, take: 1, bankContactId: null, q: "needle", TestContext.Current.CancellationToken);
        var secondPage = await sut.ListAsync(owner, skip: 1, take: 1, bankContactId: null, q: "needle", TestContext.Current.CancellationToken);
        var statistics = await sut.GetStatisticsAsync(owner, "needle", TestContext.Current.CancellationToken);

        Assert.Equal("AAA Needle", Assert.Single(firstPage).Name);
        Assert.Equal("ZZZ Needle", Assert.Single(secondPage).Name);
        Assert.Equal(2, statistics.AccountCount);
        Assert.Equal(30m, statistics.TotalBalance);
    }

    /// <summary>
    /// Verifies that statistics use BookingDate, include current posting semantics and ignore ValutaDate for period boundaries.
    /// </summary>
    [Fact]
    public async Task GetStatistics_UsesBookingDateAndIncludesCurrentPostingSemantics()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank", ContactType.Bank, null);
        var account = new Account(owner, AccountType.Giro, "Main", "DE11", bankContact.Id);
        db.Contacts.Add(bankContact);
        db.Accounts.Add(account);
        var preliminary = new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 2), new DateTime(2025, 12, 31), 7m, null, null, null, null);
        preliminary.SetIsPreliminary(true);
        var original = new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 3), 10m);
        var reversal = new Posting(Guid.NewGuid(), PostingKind.Bank, account.Id, null, null, null, new DateTime(2026, 9, 4), -10m);
        original.SetReversedBy(reversal, owner);
        reversal.SetReversalFor(original);
        db.Postings.AddRange(preliminary, original, reversal);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var statistics = await sut.GetStatisticsAsync(owner, q: null, TestContext.Current.CancellationToken);

        Assert.Equal(7m, statistics.YearToDateChange);
        Assert.Equal(7m, statistics.MonthToDateChange);
    }

    /// <summary>
    /// Verifies that mixed positive, negative and zero balances keep signed net values and gross chart volume.
    /// </summary>
    [Fact]
    public async Task GetStatistics_MixedPositiveNegativeAndZeroBalances_IsLossless()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Main Bank", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        var positive = new Account(owner, AccountType.Giro, "Positive", null, bankContact.Id);
        positive.AdjustBalance(100m);
        var negative = new Account(owner, AccountType.Giro, "Negative", null, bankContact.Id);
        negative.AdjustBalance(-100m);
        var zero = new Account(owner, AccountType.Savings, "Zero", null, bankContact.Id);
        db.Accounts.AddRange(positive, negative, zero);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var statistics = await sut.GetStatisticsAsync(owner, q: null, TestContext.Current.CancellationToken);
        var giro = Assert.Single(statistics.ByAccountType, g => g.Key == AccountType.Giro.ToString());

        Assert.Equal(0m, statistics.TotalBalance);
        Assert.Equal(200m, statistics.TotalGrossMagnitude);
        Assert.Equal(0m, giro.NetBalance);
        Assert.Equal(100m, giro.PositiveBalance);
        Assert.Equal(100m, giro.NegativeBalanceMagnitude);
        Assert.Equal(200m, giro.GrossMagnitude);
        Assert.Equal(2, giro.AccountCount);
        Assert.Equal(statistics.TotalBalance, statistics.ByAccountType.Sum(g => g.NetBalance));
        Assert.Equal(statistics.TotalGrossMagnitude, statistics.ByAccountType.Sum(g => g.GrossMagnitude));
    }

    /// <summary>
    /// Verifies orphaned bank-contact references are grouped under the stable fallback key.
    /// </summary>
    [Fact]
    public async Task GetStatistics_UnknownBankContact_UsesStableFallbackGroup()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var orphanedAccount = new Account(owner, AccountType.Giro, "Orphaned", null, Guid.NewGuid());
        orphanedAccount.AdjustBalance(12m);
        db.Accounts.Add(orphanedAccount);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var statistics = await sut.GetStatisticsAsync(owner, q: null, TestContext.Current.CancellationToken);
        var group = Assert.Single(statistics.ByBankContact);

        Assert.Equal("unknown", group.Key);
        Assert.Null(group.DisplayName);
        Assert.Equal(12m, group.NetBalance);
    }
}
