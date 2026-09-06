namespace FinanceManager.Tests.E2E;

/// <summary>
/// Drives the generic list page (currently only exercised for <c>/list/accounts</c>), which renders either
/// as a table (desktop) or as a stack of cards (mobile) depending on viewport. Tests use this gateway so
/// they can interact with "the account row" without caring which of the two markup shapes is currently
/// rendered - every method here matches both <c>.generic-list-mobile-card</c> and <c>tbody tr</c>.
/// </summary>
public sealed class ListPageGateway
{
    private readonly IPage _page;

    /// <summary>Creates the gateway for the given page.</summary>
    /// <param name="page">The Playwright page to drive.</param>
    public ListPageGateway(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the accounts list page and waits (up to 30s) for at least one row/card to be visible,
    /// so callers can rely on the list being populated before interacting with it.
    /// </summary>
    public async Task OpenAccountsAsync()
    {
        await _page.GotoAsync("/list/accounts");
        await _page.Locator(".generic-list-mobile-card:visible, tbody tr:visible").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Navigates to the accounts list page without requiring a row, useful for empty-list scenarios.
    /// </summary>
    public async Task OpenAccountsExpectingEmptyAsync()
    {
        await _page.GotoAsync("/list/accounts");
        await StatisticsTile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Waits until the account statistics tile reaches the requested state.
    /// </summary>
    /// <param name="state">Expected statistics state name.</param>
    public async Task WaitForStatisticsStateAsync(string state)
    {
        await StatisticsTile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await _page.Locator($".accounts-statistics[data-statistics-state='{state}']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Sets the list search text and waits for the first matching account to become visible.
    /// </summary>
    /// <param name="search">Search text to type into the list filter.</param>
    /// <param name="visibleAccountText">Account text expected after filtering.</param>
    public async Task SearchAccountsAsync(string search, string visibleAccountText)
    {
        await _page.Locator("input.list-filter-search").FillAsync(search);
        await WaitForAccountVisibleAsync(visibleAccountText);
        await WaitForStatisticsStateAsync("Loaded");
    }

    /// <summary>
    /// Clears the account list search through the ribbon action.
    /// </summary>
    public async Task ClearSearchAsync()
    {
        await _page.Locator("button#ClearSearch").ClickAsync();
        await WaitForStatisticsStateAsync("Loaded");
    }

    /// <summary>
    /// Clicks the statistics retry button.
    /// </summary>
    public async Task RetryStatisticsAsync()
    {
        await StatisticsTile.Locator("button").ClickAsync();
    }

    /// <summary>
    /// Scrolls to the infinite-scroll sentinel and waits until the expected account is loaded.
    /// </summary>
    public async Task LoadMoreUntilAccountVisibleAsync(string text)
    {
        await _page.Locator(".infinite-sentinel").Last.ScrollIntoViewIfNeededAsync();
        await WaitForAccountVisibleAsync(text);
    }

    /// <summary>
    /// Waits (up to 30s) for a row/card whose text contains <paramref name="text"/> to become visible,
    /// without clicking it. Useful for asserting that a newly created or renamed account has appeared in
    /// the list after a Blazor Server round-trip.
    /// </summary>
    /// <param name="text">Substring to match against the row's/card's text content, typically an account name.</param>
    public async Task WaitForAccountVisibleAsync(string text)
    {
        await GetAccountRowLocator(text).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Counts currently visible account rows/cards containing the given text.
    /// </summary>
    public Task<int> CountVisibleRowsAsync(string text)
        => _page.Locator(".generic-list-mobile-card:visible, tbody tr:visible").Filter(new() { HasText = text }).CountAsync();

    /// <summary>
    /// Waits (up to 30s) for the row/card matching <paramref name="text"/> to become visible and clicks it,
    /// which is expected to navigate to that account's detail page.
    /// </summary>
    /// <param name="text">Substring to match against the row's/card's text content, typically an account name.</param>
    public async Task OpenRowAsync(string text)
    {
        var row = GetAccountRowLocator(text);
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await row.ClickAsync();
    }

    /// <summary>The account statistics tile locator.</summary>
    public ILocator StatisticsTile => _page.Locator(".accounts-statistics");

    /// <summary>
    /// Returns a statistics KPI locator by its stable key.
    /// </summary>
    /// <param name="key">KPI key, e.g. <c>Total</c>, <c>Year</c> or <c>Month</c>.</param>
    public ILocator StatisticsKpi(string key) => _page.Locator($"[data-statistics-kpi='{key}']");

    /// <summary>
    /// Returns a statistics group locator by its stable key.
    /// </summary>
    /// <param name="key">Group key rendered by the donut legend.</param>
    public ILocator StatisticsGroup(string key) => _page.Locator($"[data-statistics-group-key='{key}']");

    /// <summary>
    /// Asserts that the current viewport has no horizontal document overflow.
    /// </summary>
    public async Task AssertNoHorizontalOverflowAsync()
    {
        var hasOverflow = await _page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        var diagnostics = await _page.EvaluateAsync<string>(
            @"() => {
                const width = document.documentElement.clientWidth;
                const offenders = [...document.querySelectorAll('body *')]
                    .map(el => {
                        const rect = el.getBoundingClientRect();
                        return {
                            tag: el.tagName.toLowerCase(),
                            className: String(el.className || ''),
                            id: el.id || '',
                            left: Math.round(rect.left),
                            right: Math.round(rect.right),
                            width: Math.round(rect.width),
                            text: (el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 80)
                        };
                    })
                    .filter(x => x.width > 0 && x.right > width + 1)
                    .slice(0, 8);
                return `client=${width}; scroll=${document.documentElement.scrollWidth}; offenders=${JSON.stringify(offenders)}`;
            }");
        hasOverflow.Should().BeFalse(diagnostics);
    }

    private ILocator GetAccountRowLocator(string text)
        => _page.Locator(".generic-list-mobile-card:visible, tbody tr:visible").Filter(new() { HasText = text }).First;
}
