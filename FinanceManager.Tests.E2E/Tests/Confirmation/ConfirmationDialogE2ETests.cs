namespace FinanceManager.Tests.E2E;

/// <summary>
/// End-to-end coverage for the unified confirmation dialog on destructive actions.
/// </summary>
[Collection(PlaywrightCollection.CollectionName)]
public sealed class ConfirmationDialogE2ETests
{
    private readonly PlaywrightWebAppFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmationDialogE2ETests"/> class.
    /// </summary>
    /// <param name="fixture">Shared Playwright web app fixture.</param>
    public ConfirmationDialogE2ETests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Clicking the ribbon Delete button on an account card opens the confirmation dialog.
    /// Cancelling keeps the account; confirming deletes it and navigates back to the list.
    /// </summary>
    [Fact]
    public async Task AccountDelete_RibbonAction_ShowsConfirmationAndDeletesOnConfirm()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var user = await LoginNewUserAsync(page, "confirmation-delete");
        var seed = new AccountsApiSeedHelper(page, _fixture.DatabasePath, user.Id);

        var unique = Guid.NewGuid().ToString("N");
        var accountName = $"Delete Me {unique}";
        var account = await seed.CreateAccountAsync(accountName, $"DE991001001000000{unique[..8]}");

        await page.GotoAsync($"/card/accounts/{account.Id}");
        await page.Locator(".card-view").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var deleteButton = page.Locator("#Delete");
        await deleteButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // 1. Cancel the delete: dialog should disappear and the account should still exist.
        await deleteButton.ClickAsync();
        var dialog = page.Locator(".confirm-dialog");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        await Microsoft.Playwright.Assertions.Expect(dialog.Locator("#confirm-title")).ToBeVisibleAsync();

        await dialog.Locator(".confirm-dialog-actions button.secondary").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30000 });

        var currentUrl = page.Url;
        currentUrl.Should().Contain($"/card/accounts/{account.Id}");

        // 2. Confirm the delete: dialog should close and the app should navigate away from the card.
        await deleteButton.ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        await dialog.Locator(".confirm-dialog-actions button:not(.secondary)").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30000 });

        await page.WaitForURLAsync("**/list/accounts");

        // The account should no longer appear in the list.
        var list = new ListPageGateway(page);
        await list.OpenAccountsExpectingEmptyAsync();
        var matchingRows = await list.CountVisibleRowsAsync(accountName);
        matchingRows.Should().Be(0, $"deleted account '{accountName}' should not appear in the list");
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
}
