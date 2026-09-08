using System.Text.RegularExpressions;
using System.Net.Http.Json;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// End-to-end regression test for the demo-data seeded budget report using a real browser session:
/// register first user with demo-data generation, wait for background task completion, open budget report,
/// move to previous month, and verify expected table positions.
/// </summary>
[Collection(PlaywrightCollection.CollectionName)]
public sealed class BudgetReportDemoDataE2ETests
{
    private readonly PlaywrightWebAppFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetReportDemoDataE2ETests"/> class.
    /// </summary>
    /// <param name="fixture">Shared Playwright web app fixture.</param>
    public BudgetReportDemoDataE2ETests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Registers a fresh first user, triggers demo-data creation, waits until the background task panel
    /// disappears, then validates that the previous-month budget report contains all expected positions.
    /// </summary>
    [Fact]
    public async Task DemoDataBudgetReport_PreviousMonth_ShouldContainAllExpectedRows()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var auth = new AuthGateway(page, _fixture.BaseUrl);

        var username = $"demo-budget-e2e-{Guid.NewGuid():N}";
        const string password = "Secret123";
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = new HttpClient
        {
            BaseAddress = new Uri(_fixture.BaseUrl),
        };
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(username, password, "de", "Europe/Berlin", CreateDemoData: true),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var backgroundPanel = page.Locator(".bgt-panel");

        await auth.LogoutAsync();
        await auth.LoginThroughUiAsync(username, password);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await backgroundPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await backgroundPanel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 240_000 });

        await page.GotoAsync("/list/budgets");
        await page.Locator("#BudgetReport").ClickAsync();
        await page.WaitForURLAsync("**/reports/budget");

        await WaitForBudgetReportReadyAsync(page);
        await page.Locator("#PrevMonth").ClickAsync();
        await WaitForBudgetReportReadyAsync(page);

        var allRowsText = await page.Locator(".budget-report-table tbody tr").AllInnerTextsAsync();
        var normalized = allRowsText
            .Select(NormalizeWhitespace)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        normalized.Should().Contain(row => row.Contains("Gehalt", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Rückstellung Hausratversicherung", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Hausratversicherung", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Rückstellung SDAC", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("SDAC", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Wohnungsmiete", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Supermärkte & Einzelhandel", StringComparison.Ordinal));
        normalized.Should().Contain(row => row.Contains("Bäckereien & Cafés", StringComparison.Ordinal));
    }

    private static async Task WaitForBudgetReportReadyAsync(IPage page)
    {
        await page.Locator(".budget-report-table").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.Locator(".budget-report-loading").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 60_000 });
    }

    private static string NormalizeWhitespace(string input)
        => Regex.Replace(input, "\\s+", " ").Trim();
}
