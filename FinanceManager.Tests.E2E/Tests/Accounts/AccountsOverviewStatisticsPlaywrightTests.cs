using System.Text.RegularExpressions;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// End-to-end coverage for the account overview statistics tile rendered above the bank account list.
/// </summary>
[Collection(PlaywrightCollection.CollectionName)]
public sealed class AccountsOverviewStatisticsPlaywrightTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountsOverviewStatisticsPlaywrightTests"/> class.
    /// </summary>
    /// <param name="fixture">Shared Playwright web app fixture.</param>
    public AccountsOverviewStatisticsPlaywrightTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Shows total/year/month statistics, lossless signed group details and the account table together.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_ShowsStatisticsAlongsideTable()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-overview");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var yearStart = new DateTime(today.Year, 1, 1);

        var unique = Guid.NewGuid().ToString("N");
        var primaryName = $"Stats Primary {unique}";
        var savingsName = $"Stats Savings {unique}";
        var zeroName = $"Stats Zero {unique}";
        await seed.CreateAccountWithBalanceAsync(
            primaryName,
            "DE44100100100000000001",
            AccountType.Giro,
            150m,
            "Stats Primary Bank",
            [
                new AccountsApiSeedHelper.SeededPosting(yearStart, 10m),
                new AccountsApiSeedHelper.SeededPosting(monthStart, 5m),
                new AccountsApiSeedHelper.SeededPosting(today.AddDays(1), 999m)
            ]);
        await seed.CreateAccountWithBalanceAsync(
            savingsName,
            "DE44100100100000000002",
            AccountType.Savings,
            -40m,
            "Stats Savings Bank",
            [
                new AccountsApiSeedHelper.SeededPosting(yearStart.AddDays(1), -3m),
                new AccountsApiSeedHelper.SeededPosting(today, -2m)
            ]);
        await seed.CreateAccountWithBalanceAsync(zeroName, "DE44100100100000000003", AccountType.Giro, 0m, "Stats Zero Bank");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForAccountVisibleAsync(primaryName);
        await list.WaitForStatisticsStateAsync("Loaded");

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 110);
        await ExpectAmountAsync(list.StatisticsKpi("Year"), 10);
        await ExpectAmountAsync(list.StatisticsKpi("Month"), 3);
        await ExpectAmountAsync(list.StatisticsGroup(nameof(AccountType.Giro)), 150);
        await ExpectAmountAsync(list.StatisticsGroup(nameof(AccountType.Savings)), -40);
        await ExpectTextAsync(list.StatisticsTile, "Positive");
        await ExpectTextAsync(list.StatisticsTile, "Negative");
        await ExpectTextAsync(list.StatisticsTile, "Zero");
    }

    /// <summary>
    /// Search updates the visible table rows and the unpaged statistics with the same normalized query.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_Search_UpdatesTableAndStatisticsTogether()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-search");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var unique = Guid.NewGuid().ToString("N");
        var targetName = $"ZZ Search Target {unique}";
        var nonMatchName = $"AA Search Filler {unique}";

        for (var i = 0; i < 55; i++)
        {
            await seed.CreateAccountWithBalanceAsync(
                $"{nonMatchName} {i:00}",
                $"DE991001001000000{i:00000}",
                AccountType.Giro,
                1m,
                $"Filler Bank {i:00}");
        }

        await seed.CreateAccountWithBalanceAsync(
            targetName,
            "DE99 1111-2222",
            AccountType.Savings,
            75m,
            "Search Target Bank");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 130);

        await list.SearchAccountsAsync("de9911112222", targetName);
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 75);
        await ExpectTextAsync(list.StatisticsTile, "Search Target Bank");
        var nonMatchingRows = await page.Locator(".generic-list-mobile-card:visible, tbody tr:visible")
            .Filter(new() { HasText = nonMatchName })
            .CountAsync();
        nonMatchingRows.Should().Be(0);

        await list.ClearSearchAsync();
        await list.WaitForAccountVisibleAsync(nonMatchName);
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 130);
    }

    /// <summary>
    /// KPI details show total, current year and current month changes with known amounts.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_ShowsTotalYearChangeAndMonthChange()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-kpis");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var today = DateTime.UtcNow.Date;
        var unique = Guid.NewGuid().ToString("N");

        await seed.CreateAccountWithBalanceAsync(
            $"KPI Account {unique}",
            "DE33100100100000000001",
            AccountType.Giro,
            500m,
            "KPI Bank",
            [
                new AccountsApiSeedHelper.SeededPosting(new DateTime(today.Year, 1, 1), 100m),
                new AccountsApiSeedHelper.SeededPosting(new DateTime(today.Year, today.Month, 1), -20m),
                new AccountsApiSeedHelper.SeededPosting(today.AddDays(1), 999m)
            ]);

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 500);
        await ExpectAmountAsync(list.StatisticsKpi("Year"), 80);
        await ExpectAmountAsync(list.StatisticsKpi("Month"), -20);
    }

    /// <summary>
    /// Both donut charts render their titles, deterministic groups and center net balance.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_ShowsAccountTypeAndBankContactDonuts()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-donuts");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var first = await seed.CreateAccountWithBalanceAsync($"Donut Giro {Guid.NewGuid():N}", "DE44100100100000000011", AccountType.Giro, 120m, "Donut Bank A");
        var second = await seed.CreateAccountWithBalanceAsync($"Donut Savings {Guid.NewGuid():N}", "DE44100100100000000012", AccountType.Savings, 30m, "Donut Bank B");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");

        await ExpectTextAnyAsync(list.StatisticsTile, "By account type", "Nach Kontoart");
        await ExpectTextAnyAsync(list.StatisticsTile, "By bank contact", "Nach Bankkontakt");
        await ExpectTextAnyAsync(list.StatisticsTile, "Net balance", "Nettosaldo");
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(nameof(AccountType.Giro))).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(nameof(AccountType.Savings))).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(first.BankContactId.ToString("D"))).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(second.BankContactId.ToString("D"))).ToBeVisibleAsync();
    }

    /// <summary>
    /// Mixed positive, negative and zero balances remain visible with signed net and gross basis details.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_MixedBalances_ShowsLosslessSignedDistribution()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-mixed");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var contactId = await seed.CreateBankContactAsync("Mixed Signs Bank");
        var unique = Guid.NewGuid().ToString("N");

        await seed.CreateAccountWithBalanceAsync($"Mixed Positive {unique}", "DE66100100100000000001", AccountType.Giro, 100m, "Mixed Signs Bank", bankContactId: contactId);
        await seed.CreateAccountWithBalanceAsync($"Mixed Negative {unique}", "DE66100100100000000002", AccountType.Giro, -100m, "Mixed Signs Bank", bankContactId: contactId);
        await seed.CreateAccountWithBalanceAsync($"Mixed Zero {unique}", "DE66100100100000000003", AccountType.Savings, 0m, "Mixed Signs Bank", bankContactId: contactId);

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 0);
        await ExpectAmountAsync(list.StatisticsGroup(nameof(AccountType.Giro)), 0);
        await ExpectTextAnyAsync(list.StatisticsTile, "Positive balances", "Positive Salden");
        await ExpectTextAnyAsync(list.StatisticsTile, "Negative balances", "Negative Salden");
        await ExpectTextAnyAsync(list.StatisticsTile, "Zero balances", "Nullsalden");
        await ExpectTextAnyAsync(list.StatisticsTile, "Share of absolute balance volume", "Anteil am absoluten Saldenvolumen");
        (await list.StatisticsTile.Locator(".donut-segment").CountAsync()).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Unknown and very long bank contact labels remain readable on desktop and mobile.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_UnknownAndLongBankContact_RemainsReadable()
    {
        var username = $"accounts-stats-long-{Guid.NewGuid():N}";
        const string password = "Secret123";
        var longBankName = $"Very Long Desktop And Mobile Bank Contact Name {Guid.NewGuid():N} With Several Readable Segments";

        await using (var session = await _fixture.CreateSessionAsync())
        {
            var page = session.Page;
            var user = await LoginExistingUserAsync(page, username, password);
            var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
            await seed.CreateAccountWithUnknownBankContactAsync($"Unknown Contact Account {Guid.NewGuid():N}", "DE77100100100000000001", AccountType.Giro, 15m);
            await seed.CreateAccountWithBalanceAsync($"Long Contact Account {Guid.NewGuid():N}", "DE77100100100000000002", AccountType.Savings, 25m, longBankName);

            var list = new ListPageGateway(page);
            await list.OpenAccountsAsync();
            await list.WaitForStatisticsStateAsync("Loaded");
            await ExpectTextAnyAsync(list.StatisticsTile, "Unknown bank contact", "Unbekannter Bankkontakt");
            await ExpectTextAsync(list.StatisticsTile, longBankName);
        }

        await using (var session = await _fixture.CreateMobileSessionAsync())
        {
            var page = session.Page;
            await LoginExistingUserAsync(page, username, password);
            var list = new ListPageGateway(page);
            await list.OpenAccountsAsync();
            await list.WaitForStatisticsStateAsync("Loaded");
            await ExpectTextAnyAsync(list.StatisticsTile, "Unknown bank contact", "Unbekannter Bankkontakt");
            await ExpectTextAsync(list.StatisticsTile, longBankName);
            await list.AssertNoHorizontalOverflowAsync();
        }
    }

    /// <summary>
    /// ClearSearch restores the unfiltered table, KPI total and donut groups.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_ClearSearch_RestoresTableAndStatistics()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-clear");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var unique = Guid.NewGuid().ToString("N");
        var alpha = $"Clear Alpha {unique}";
        var beta = $"Clear Beta {unique}";
        await seed.CreateAccountWithBalanceAsync(alpha, "DE88100100100000000001", AccountType.Giro, 10m, "Clear Bank A");
        await seed.CreateAccountWithBalanceAsync(beta, "DE88100100100000000002", AccountType.Savings, 20m, "Clear Bank B");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");
        await list.SearchAccountsAsync(alpha, alpha);
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 10);
        (await list.CountVisibleRowsAsync(beta)).Should().Be(0);

        await list.ClearSearchAsync();

        await list.WaitForAccountVisibleAsync(beta);
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 30);
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(nameof(AccountType.Giro))).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsGroup(nameof(AccountType.Savings))).ToBeVisibleAsync();
    }

    /// <summary>
    /// Infinite scroll loads more table rows without changing the unpaged statistics total.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_InfiniteScroll_DoesNotChangeStatisticsTotal()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-scroll");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var unique = Guid.NewGuid().ToString("N");
        var lastName = $"ZZ Scroll Account {unique} 54";
        for (var i = 0; i < 55; i++)
        {
            await seed.CreateAccountWithBalanceAsync(
                i == 54 ? lastName : $"AA Scroll Account {unique} {i:00}",
                $"DE891001001000000{i:00000}",
                AccountType.Giro,
                1m,
                "Scroll Bank");
        }

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 55);

        await list.LoadMoreUntilAccountVisibleAsync(lastName);

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 55);
    }

    /// <summary>
    /// Accounts belonging to another user never appear in rows, KPIs or chart legends.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_OtherUsersAccountsRemainInvisibleEverywhere()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-owner");
        var ownSeed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var foreignUser = await new TestUserSeeder(_fixture.DatabasePath).EnsureUserAsync($"foreign-{Guid.NewGuid():N}", "Secret123", timeZoneId: "UTC");
        var foreignSeed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, foreignUser.Id);
        var foreignName = $"Foreign Hidden {Guid.NewGuid():N}";
        await ownSeed.CreateAccountWithBalanceAsync($"Own Visible {Guid.NewGuid():N}", "DE90100100100000000001", AccountType.Giro, 5m, "Own Bank");
        await foreignSeed.CreateAccountWithBalanceAsync(foreignName, "DE90100100100000000002", AccountType.Savings, 999m, "Foreign Bank");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 5);
        (await list.CountVisibleRowsAsync(foreignName)).Should().Be(0);
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsTile).Not.ToContainTextAsync("Foreign Bank");
        await Microsoft.Playwright.Assertions.Expect(list.StatisticsTile).Not.ToContainTextAsync("999");
    }

    /// <summary>
    /// Empty account scope shows zero KPIs, empty charts and keeps ribbon actions usable.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_Empty_ShowsZeroKpisEmptyChartsAndUsableRibbon()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        await LoginNewUserAsync(page, "accounts-stats-empty");

        var list = new ListPageGateway(page);
        await list.OpenAccountsExpectingEmptyAsync();
        await list.WaitForStatisticsStateAsync("Empty");

        await ExpectAmountAsync(list.StatisticsKpi("Total"), 0);
        await ExpectAmountAsync(list.StatisticsKpi("Year"), 0);
        await ExpectAmountAsync(list.StatisticsKpi("Month"), 0);
        await ExpectTextAnyAsync(list.StatisticsTile, "No accounts for this selection", "Keine Konten für diese Auswahl");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("button#New")).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("button#ClearSearch")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A statistics failure is isolated to the tile; table search, retry and row navigation remain usable.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_StatisticsFailure_ShowsErrorAndLeavesTableUsable()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-fault");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var accountName = $"Fault Account {Guid.NewGuid():N}";
        var account = await seed.CreateAccountWithBalanceAsync(accountName, "DE91100100100000000001", AccountType.Giro, 77m, "Fault Bank");
        _fixture.EnableAccountStatisticsFault();

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Error");
        await ExpectTextAnyAsync(list.StatisticsTile, "Statistics could not be loaded", "Statistik konnte nicht geladen", "Injected account statistics failure");
        await list.WaitForAccountVisibleAsync(accountName);

        await page.Locator("input.list-filter-search").FillAsync(accountName);
        await list.WaitForAccountVisibleAsync(accountName);
        _fixture.ResetAccountStatisticsFault();
        await list.RetryStatisticsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");
        await ExpectAmountAsync(list.StatisticsKpi("Total"), 77);

        await list.OpenRowAsync(accountName);
        await page.WaitForURLAsync($"**/card/accounts/{account.AccountId}");
    }

    /// <summary>
    /// Clicking an account row still navigates to the account card.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_ClickingAccountRow_StillNavigatesToCard()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-row");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var accountName = $"Row Navigation {Guid.NewGuid():N}";
        var account = await seed.CreateAccountWithBalanceAsync(accountName, "DE92100100100000000001", AccountType.Giro, 13m, "Row Bank");

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForStatisticsStateAsync("Loaded");
        await list.OpenRowAsync(accountName);

        await page.WaitForURLAsync($"**/card/accounts/{account.AccountId}");
    }

    /// <summary>
    /// Mobile layout keeps statistics, long bank contact names and account cards visible without horizontal overflow.
    /// </summary>
    [Fact]
    public async Task AccountsOverview_Mobile_RendersStatisticsWithoutHidingList()
    {
        await using var session = await _fixture.CreateMobileSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "accounts-stats-mobile");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);
        var unique = Guid.NewGuid().ToString("N");
        var accountName = $"Mobile Stats {unique}";
        var longBankName = $"Very Long Mobile Bank Contact Name {unique} With Several Readable Segments";

        await seed.CreateAccountWithBalanceAsync(
            accountName,
            "DE55100100100000000001",
            AccountType.Giro,
            42m,
            longBankName);

        var list = new ListPageGateway(page);
        await list.OpenAccountsAsync();
        await list.WaitForAccountVisibleAsync(accountName);
        await list.WaitForStatisticsStateAsync("Loaded");

        await Microsoft.Playwright.Assertions.Expect(list.StatisticsTile).ToBeVisibleAsync();
        await ExpectTextAsync(list.StatisticsTile, longBankName);
        await page.Locator(".generic-list-mobile-card").Filter(new() { HasText = accountName })
            .First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await list.AssertNoHorizontalOverflowAsync();
    }

    private async Task<FinanceManager.Domain.Users.User> LoginNewUserAsync(IPage page, string prefix)
    {
        var username = $"{prefix}-{Guid.NewGuid():N}";
        return await LoginExistingUserAsync(page, username, "Secret123");
    }

    private async Task<FinanceManager.Domain.Users.User> LoginExistingUserAsync(IPage page, string username, string password)
    {
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);
        var user = await seeder.EnsureUserAsync(username, password, timeZoneId: "UTC");
        await auth.LoginAsync(username, password);
        return user;
    }

    private static async Task ExpectAmountAsync(ILocator locator, int wholeAmount)
    {
        var pattern = wholeAmount < 0
            ? $@"-\s*{Math.Abs(wholeAmount)}([,.]00)?"
            : $@"{wholeAmount}([,.]00)?";
            await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync(new Regex(pattern));
    }

    private static async Task ExpectTextAsync(ILocator locator, string text)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync(text);
        }
        catch (PlaywrightException) when (string.Equals(text, "Positive", StringComparison.OrdinalIgnoreCase))
        {
            await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync("Positive Salden");
        }
        catch (PlaywrightException) when (string.Equals(text, "Negative", StringComparison.OrdinalIgnoreCase))
        {
            await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync("Negative Salden");
        }
        catch (PlaywrightException) when (string.Equals(text, "Zero", StringComparison.OrdinalIgnoreCase))
        {
            await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync("Nullsalden");
        }
    }

    private static async Task ExpectTextAnyAsync(ILocator locator, params string[] texts)
    {
        var pattern = string.Join("|", texts.Select(Regex.Escape));
        await Microsoft.Playwright.Assertions.Expect(locator).ToContainTextAsync(new Regex(pattern, RegexOptions.IgnoreCase));
    }
}
