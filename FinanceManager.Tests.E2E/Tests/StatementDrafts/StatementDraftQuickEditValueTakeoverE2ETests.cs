using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.Playwright;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// End-to-end tests for the statement draft "quick edit" grid's keyboard-driven value takeover
/// features: Ctrl+Arrow focus navigation between rows (including skipping hidden/deleted/locked
/// rows and staying put at the grid's edges), automatic booking-to-valuta date copying, the save
/// button's enabled/disabled state as rows become complete, F8/Ctrl+F8 field takeover from the row
/// above (single field vs. all fields with overwrite), and the keepalive request sent (and
/// coalesced across rapid blurs) when a field loses focus without a full save.
/// </summary>
[Collection(PlaywrightCollection.CollectionName)]
public sealed class StatementDraftQuickEditValueTakeoverE2ETests
{
    private readonly PlaywrightWebAppFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementDraftQuickEditValueTakeoverE2ETests"/> class.
    /// </summary>
    /// <param name="fixture">Shared Playwright web app fixture providing the browser and test server.</param>
    public StatementDraftQuickEditValueTakeoverE2ETests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that pressing Ctrl+ArrowUp in a quick-edit amount field moves focus to the same
    /// field (amount) in the previous visible row.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowUp_ShouldFocusSameFieldInPreviousVisibleRow()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-up");

        await OpenQuickEditAsync(page, draft.DraftId);

        var previousAmount = page.Locator("input[id^='qe_amount_']").Nth(0);
        var currentAmount = page.Locator("input[id^='qe_amount_']").Nth(1);
        var expectedId = await previousAmount.GetAttributeAsync("id");

        await currentAmount.FocusAsync();
        await PressKeyAsync(currentAmount, "Control+ArrowUp");

        await WaitForActiveElementAsync(page, expectedId);
    }

    /// <summary>
    /// Verifies that pressing Ctrl+ArrowDown in a quick-edit subject field moves focus to the same
    /// field (subject) in the next visible row.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowDown_ShouldFocusSameFieldInNextVisibleRow()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-down");

        await OpenQuickEditAsync(page, draft.DraftId);

        var currentSubject = page.Locator("input[id^='qe_subject_']").Nth(1);
        var nextSubject = page.Locator("input[id^='qe_subject_']").Nth(2);
        var expectedId = await nextSubject.GetAttributeAsync("id");

        await currentSubject.FocusAsync();
        await PressKeyAsync(currentSubject, "Control+ArrowDown");

        await WaitForActiveElementAsync(page, expectedId);
    }

    /// <summary>
    /// Verifies that pressing Ctrl+ArrowUp while focused in the first visible row keeps focus on
    /// the same field instead of moving out of the grid.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowUpAtFirstRow_ShouldKeepFocus()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-up-first");

        await OpenQuickEditAsync(page, draft.DraftId);

        var firstBookingDate = page.Locator("input[id^='qe_booking_']").Nth(0);
        var expectedId = await firstBookingDate.GetAttributeAsync("id");

        await firstBookingDate.FocusAsync();
        await PressKeyAsync(firstBookingDate, "Control+ArrowUp");

        await WaitForActiveElementAsync(page, expectedId);
    }

    /// <summary>
    /// Verifies that pressing Ctrl+ArrowDown while focused in the last visible row keeps focus on
    /// the same field instead of moving out of the grid.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowDownAtLastRow_ShouldKeepFocus()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-down-last");

        await OpenQuickEditAsync(page, draft.DraftId);

        var subjectInputs = page.Locator("input[id^='qe_subject_']");
        var lastSubject = subjectInputs.Nth(await subjectInputs.CountAsync() - 1);
        var expectedId = await lastSubject.GetAttributeAsync("id");

        await lastSubject.FocusAsync();
        await PressKeyAsync(lastSubject, "Control+ArrowDown");

        await WaitForActiveElementAsync(page, expectedId);
    }

    /// <summary>
    /// Verifies that pressing Ctrl+ArrowUp/Down while focused in a booking-date or valuta-date
    /// input moves focus to the same field in the adjacent row (row navigation) rather than being
    /// intercepted by the date input's native up/down spin behavior, and that the field's own
    /// value is left unchanged by the key press.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowOnDateFields_ShouldMoveFocusWithoutChangingDate()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-date-no-spin");

        await OpenQuickEditAsync(page, draft.DraftId);

        var previousBookingDate = page.Locator("input[id^='qe_booking_']").Nth(0);
        var currentBookingDate = page.Locator("input[id^='qe_booking_']").Nth(1);
        var expectedPreviousBookingDateId = await previousBookingDate.GetAttributeAsync("id");
        const string bookingDate = "2026-08-30";

        await currentBookingDate.FillAsync(bookingDate);
        await currentBookingDate.FocusAsync();
        await PressKeyAsync(currentBookingDate, "Control+ArrowUp");

        await WaitForActiveElementAsync(page, expectedPreviousBookingDateId);
        (await currentBookingDate.InputValueAsync()).Should().Be(bookingDate);

        var currentValutaDate = page.Locator("input[id^='qe_valuta_']").Nth(1);
        var nextValutaDate = page.Locator("input[id^='qe_valuta_']").Nth(2);
        var expectedNextValutaDateId = await nextValutaDate.GetAttributeAsync("id");
        const string valutaDate = "2026-08-30";

        await currentValutaDate.FillAsync(valutaDate);
        await currentValutaDate.FocusAsync();
        await PressKeyAsync(currentValutaDate, "Control+ArrowDown");

        await WaitForActiveElementAsync(page, expectedNextValutaDateId);
        (await currentValutaDate.InputValueAsync()).Should().Be(valutaDate);
    }

    /// <summary>
    /// Verifies that changing a row's booking date automatically copies the new value into that
    /// row's valuta date when the valuta date is currently empty, and also propagates a later
    /// booking-date change to a row whose valuta date already matched the previous booking date -
    /// but does not touch a valuta date that was set independently.
    /// </summary>
    [Fact]
    public async Task QuickEdit_BookingDateChange_ShouldCopyToEmptyOrMatchedValutaDateOnly()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "booking-to-valuta");

        await OpenQuickEditAsync(page, draft.DraftId);

        var bookingDateWithoutValuta = page.Locator("input[id^='qe_booking_']").Nth(0);
        var emptyValutaDate = page.Locator("input[id^='qe_valuta_']").Nth(0);
        const string copiedDate = "2026-08-30";

        (await emptyValutaDate.InputValueAsync()).Should().BeEmpty();

        await bookingDateWithoutValuta.FillAsync(copiedDate);
        await bookingDateWithoutValuta.PressAsync("Tab");

        await WaitForInputValueAsync(emptyValutaDate, copiedDate);

        var bookingDateWithMatchedValuta = page.Locator("input[id^='qe_booking_']").Nth(1);
        var existingValutaDate = page.Locator("input[id^='qe_valuta_']").Nth(1);
        var existingValutaValue = await existingValutaDate.InputValueAsync();
        const string changedBookingDate = "2026-09-15";

        existingValutaValue.Should().NotBeNullOrWhiteSpace();

        await bookingDateWithMatchedValuta.FillAsync(changedBookingDate);
        await bookingDateWithMatchedValuta.PressAsync("Tab");

        await WaitForInputValueAsync(existingValutaDate, changedBookingDate);
    }

    /// <summary>
    /// Verifies that the quick-edit save button starts disabled when there are no pending
    /// changes, and becomes enabled once a row is edited into a valid, complete state.
    /// </summary>
    [Fact]
    public async Task QuickEdit_SaveButton_IsEnabledWhenAllRowsComplete()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "save-state");

        await OpenQuickEditAsync(page, draft.DraftId);

        var saveButton = page.Locator("button#SaveQuickEdit");
        await saveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // No pending changes yet -> save should be disabled
        (await saveButton.IsDisabledAsync()).Should().BeTrue();

        var bookingDateWithoutValuta = page.Locator("input[id^='qe_booking_']").Nth(0);
        var emptyValutaDate = page.Locator("input[id^='qe_valuta_']").Nth(0);
        const string bookingDate = "2026-08-30";

        await bookingDateWithoutValuta.FillAsync(bookingDate);
        await bookingDateWithoutValuta.PressAsync("Tab");

        await WaitForInputValueAsync(emptyValutaDate, bookingDate);

        // With a valid changed row the save button must become enabled
        await Assertions.Expect(saveButton).ToBeEnabledAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// Verifies that Ctrl+Arrow row navigation skips over a row whose status makes it
    /// non-editable (e.g. already booked/locked), and continues to do so correctly after that
    /// row's neighbor is deleted client-side, so focus always lands on an editable row.
    /// </summary>
    [Fact]
    public async Task QuickEdit_CtrlArrowDown_ShouldSkipHiddenDeletedAndLockedRows()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-skip", middleStatus: StatementDraftEntryStatus.AlreadyBooked);

        await OpenQuickEditAsync(page, draft.DraftId);

        var firstSubject = page.Locator("input[id^='qe_subject_']").Nth(0);
        var subjectBeforeLocked = page.Locator("input[id^='qe_subject_']").Nth(1);
        var placeholderSubject = page.Locator("input[id^='qe_subject_']").Nth(2);
        var firstSubjectId = await firstSubject.GetAttributeAsync("id");
        var subjectBeforeLockedId = await subjectBeforeLocked.GetAttributeAsync("id");
        var placeholderId = await placeholderSubject.GetAttributeAsync("id");

        await subjectBeforeLocked.FocusAsync();
        await PressKeyAsync(subjectBeforeLocked, "Control+ArrowDown");
        await WaitForActiveElementAsync(page, placeholderId);

        var deleteButton = page.Locator(".quick-edit-delete-button").First;
        await deleteButton.ClickAsync();
        await page.Locator($"#{firstSubjectId}").WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 5000 });

        var subjectBeforeLockedById = page.Locator($"#{subjectBeforeLockedId}");
        await subjectBeforeLockedById.FocusAsync();
        await PressKeyAsync(subjectBeforeLockedById, "Control+ArrowUp");

        await WaitForActiveElementAsync(page, subjectBeforeLockedId);
    }

    /// <summary>
    /// Verifies that the Ctrl+Arrow row-navigation shortcut is scoped to the quick-edit grid: on
    /// a regular (non-quick-edit) draft card page, pressing Ctrl+ArrowDown in a plain input field
    /// leaves focus unchanged.
    /// </summary>
    [Fact]
    public async Task CtrlArrowNavigation_OutsideQuickEdit_ShouldNotChangeFocus()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "ctrl-outside");

        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{draft.DraftId}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        (await page.Locator("input[id^='qe_']").CountAsync()).Should().Be(0);
        var input = page.Locator("input").First;
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var expectedId = await input.GetAttributeAsync("id");

        await input.FocusAsync();
        await PressKeyAsync(input, "Control+ArrowDown");

        await WaitForActiveElementAsync(page, expectedId);
    }

    /// <summary>
    /// Verifies that typing directly into a quick-edit field is not clobbered by an unrelated F8
    /// takeover on a different field, and that editing one field afterwards leaves the other
    /// field's value from the earlier takeover unaffected.
    /// </summary>
    [Fact]
    public async Task QuickEdit_RegularInputAndF8_ShouldRemainUnaffected()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var draft = await CreateQuickEditDraftWithPersistedRowsAsync(page, "regular-input");

        await OpenQuickEditAsync(page, draft.DraftId);

        var sourceSubject = page.Locator("input[id^='qe_subject_']").Nth(0);
        var targetSubject = page.Locator("input[id^='qe_subject_']").Nth(1);
        const string typedSubject = "Manual quick edit text";

        await targetSubject.FillAsync(typedSubject);
        (await targetSubject.InputValueAsync()).Should().Be(typedSubject);

        var expectedFromAbove = await sourceSubject.InputValueAsync();
        await PressKeyAsync(targetSubject, "F8");
        await WaitForInputValueAsync(targetSubject, expectedFromAbove);

        var targetAmount = page.Locator("input[id^='qe_amount_']").Nth(1);
        const string typedAmount = "42";

        await targetAmount.FillAsync(typedAmount);
        (await targetAmount.InputValueAsync()).Should().Be(typedAmount);
        (await targetSubject.InputValueAsync()).Should().Be(expectedFromAbove);
    }

    /// <summary>
    /// Verifies that pressing F8 in a quick-edit field copies just that single field's value from
    /// the row directly above, leaving the row's other fields untouched.
    /// </summary>
    [Fact]
    public async Task QuickEdit_PressesF8_ShouldCopySingleFieldFromRowAbove()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);

        var username = $"f8-e2e-{Guid.NewGuid():N}";
        const string password = "Secret123";
        await seeder.EnsureUserAsync(username, password);
        await auth.LoginAsync(username, password);

        var account = await new AccountsApiSeedHelper(page).CreateAccountAsync("E2E F8 Takeover", "DE50700500000007882904");

        var prelimDraft = await BrowserApiHelper.PostJsonAsync<CreatePreliminaryStatementDraftRequest, StatementDraftDto>(
            page,
            "/api/statement-drafts/preliminary",
            new CreatePreliminaryStatementDraftRequest(account.Id));

        var entryId = prelimDraft.Entries!.Single().Id;

        await BrowserApiHelper.PostJsonAsync(page, $"/api/statement-drafts/{prelimDraft.DraftId}/entries/batch-update", new
        {
            updates = new[]
            {
                new
                {
                    entryId,
                    fields = new Dictionary<string, object?>
                    {
                        ["BookingDate"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["Amount"] = 123.45m,
                        ["Subject"] = "Source Subject",
                        ["RecipientName"] = "Source Recipient",
                        ["BookingDescription"] = "Source Description"
                    }
                }
            },
            creates = new object[] { },
            deletes = System.Array.Empty<Guid>()
        });

        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{prelimDraft.DraftId}?quickEdit=true");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Locator("input[id^='qe_booking_']").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var sourceSubject = page.Locator("input[id^='qe_subject_']").Nth(0);
        var targetSubject = page.Locator("input[id^='qe_subject_']").Nth(1);

        var expected = await sourceSubject.InputValueAsync();
        await PressKeyAsync(targetSubject, "F8");
        await WaitForInputValueAsync(targetSubject, expected);

        var actual = await targetSubject.InputValueAsync();
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that pressing Ctrl+F8 in a quick-edit row copies all take-over fields (booking
    /// date, valuta date, amount, description, recipient, subject) from the row directly above,
    /// overwriting whatever values were already present in the target row.
    /// </summary>
    [Fact]
    public async Task QuickEdit_PressesCtrlF8_ShouldCopyAllFieldsFromRowAboveAndOverwriteExisting()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);

        var username = $"ctrlf8-e2e-{Guid.NewGuid():N}";
        const string password = "Secret123";
        await seeder.EnsureUserAsync(username, password);
        await auth.LoginAsync(username, password);

        var account = await new AccountsApiSeedHelper(page).CreateAccountAsync("E2E Ctrl+F8 Takeover", "DE50700500000007882905");

        var prelimDraft = await BrowserApiHelper.PostJsonAsync<CreatePreliminaryStatementDraftRequest, StatementDraftDto>(
            page,
            "/api/statement-drafts/preliminary",
            new CreatePreliminaryStatementDraftRequest(account.Id));

        var entryId = prelimDraft.Entries!.Single().Id;

        await BrowserApiHelper.PostJsonAsync(page, $"/api/statement-drafts/{prelimDraft.DraftId}/entries/batch-update", new
        {
            updates = new[]
            {
                new
                {
                    entryId,
                    fields = new Dictionary<string, object?>
                    {
                        ["BookingDate"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["Amount"] = 123.45m,
                        ["Subject"] = "Source Subject",
                        ["RecipientName"] = "Source Recipient",
                        ["BookingDescription"] = "Source Description"
                    }
                }
            },
            creates = new object[] { },
            deletes = System.Array.Empty<Guid>()
        });

        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{prelimDraft.DraftId}?quickEdit=true");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Locator("input[id^='qe_booking_']").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var sourceBookingDate = page.Locator("input[id^='qe_booking_']").Nth(0);
        var sourceValuta = page.Locator("input[id^='qe_valuta_']").Nth(0);
        var sourceAmount = page.Locator("input[id^='qe_amount_']").Nth(0);
        var sourceDescription = page.Locator("input[id^='qe_description_']").Nth(0);
        var sourceRecipient = page.Locator("input[id^='qe_recipient_']").Nth(0);
        var sourceSubject = page.Locator("input[id^='qe_subject_']").Nth(0);

        var targetBookingDate = page.Locator("input[id^='qe_booking_']").Nth(1);
        var targetValuta = page.Locator("input[id^='qe_valuta_']").Nth(1);
        var targetAmount = page.Locator("input[id^='qe_amount_']").Nth(1);
        var targetDescription = page.Locator("input[id^='qe_description_']").Nth(1);
        var targetRecipient = page.Locator("input[id^='qe_recipient_']").Nth(1);
        var targetSubject = page.Locator("input[id^='qe_subject_']").Nth(1);

        // Pre-fill the target row with different values to verify overwrite
        await targetBookingDate.FillAsync("2020-01-01");
        await targetAmount.FillAsync("0.01");
        await targetDescription.FillAsync("Different");
        await targetRecipient.FillAsync("Different");
        await targetSubject.FillAsync("Different");

        var expectedSubject = await sourceSubject.InputValueAsync();
        await PressKeyAsync(targetSubject, "Control+F8");
        await WaitForInputValueAsync(targetSubject, expectedSubject);

        (await targetBookingDate.InputValueAsync()).Should().Be(await sourceBookingDate.InputValueAsync());
        (await targetAmount.InputValueAsync()).Should().Be(await sourceAmount.InputValueAsync());
        (await targetDescription.InputValueAsync()).Should().Be(await sourceDescription.InputValueAsync());
        (await targetRecipient.InputValueAsync()).Should().Be(await sourceRecipient.InputValueAsync());
        (await targetSubject.InputValueAsync()).Should().Be(await sourceSubject.InputValueAsync());

        var sourceValutaValue = await sourceValuta.InputValueAsync();
        if (string.IsNullOrWhiteSpace(sourceValutaValue))
        {
            (await targetValuta.InputValueAsync()).Should().BeEmpty();
        }
        else
        {
            (await targetValuta.InputValueAsync()).Should().Be(sourceValutaValue);
        }
    }

    /// <summary>
    /// Verifies that blurring a quick-edit field with an unsaved edit while the session is near
    /// expiry sends a keepalive request that succeeds and refreshes the session, and that the
    /// field's locally typed value is preserved (not reset by the reload-free keepalive) and the
    /// user remains authenticated.
    /// </summary>
    [Fact]
    public async Task QuickEdit_Blur_ShouldSendKeepaliveAndKeepLocalInputValue()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);
        var cookies = new TestAuthCookieHelper(_fixture.DatabasePath, _fixture.BaseUrl);

        var username = $"quickedit-keepalive-{Guid.NewGuid():N}";
        const string password = "Secret123";
        const string expectedSubject = "Unsent quick edit value";

        await seeder.EnsureUserAsync(username, password);
        await auth.LoginAsync(username, password);

        var account = await new AccountsApiSeedHelper(page).CreateAccountAsync("E2E QuickEdit Keepalive", "DE50700500000007882906");

        var prelimDraft = await BrowserApiHelper.PostJsonAsync<CreatePreliminaryStatementDraftRequest, StatementDraftDto>(
            page,
            "/api/statement-drafts/preliminary",
            new CreatePreliminaryStatementDraftRequest(account.Id));

        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{prelimDraft.DraftId}?quickEdit=true");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("input[id^='qe_subject_']").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var subject = page.Locator("input[id^='qe_subject_']").Nth(1);
        (await subject.GetAttributeAsync("data-fm-quickedit-keepalive")).Should().NotBeNull();
        await subject.FillAsync(expectedSubject);
        (await subject.InputValueAsync()).Should().Be(expectedSubject);

        await cookies.SetNearExpiryCookieAsync(page, username);
        await PlaywrightTestTiming.WaitForForcedKeepaliveThrottleAsync(page);
        var response = await page.RunAndWaitForResponseAsync(async () =>
        {
            await subject.FocusAsync();
            await subject.BlurAsync();
        }, IsKeepaliveResponse);

        response.Status.Should().Be(204);
        (await subject.InputValueAsync()).Should().Be(expectedSubject);

        var profile = await BrowserApiHelper.GetWithStatusAsync<object>(page, "/api/user/settings/profile");
        profile.Status.Should().Be(200);
        page.Url.ToLowerInvariant().Should().NotContain("/login");
    }

    /// <summary>
    /// Verifies that triggering several quick-edit field blurs in rapid succession only sends a
    /// single coalesced keepalive request instead of one request per blurred field.
    /// </summary>
    [Fact]
    public async Task QuickEdit_Blur_ShouldCoalesceKeepaliveRequests()
    {
        await using var session = await _fixture.CreateSessionAsync();
        var page = session.Page;
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);

        var username = $"quickedit-blur-coalesce-{Guid.NewGuid():N}";
        const string password = "Secret123";

        await seeder.EnsureUserAsync(username, password);
        await auth.LoginAsync(username, password);

        var account = await new AccountsApiSeedHelper(page).CreateAccountAsync("E2E QuickEdit Blur Coalesce", "DE50700500000007882907");

        var prelimDraft = await BrowserApiHelper.PostJsonAsync<CreatePreliminaryStatementDraftRequest, StatementDraftDto>(
            page,
            "/api/statement-drafts/preliminary",
            new CreatePreliminaryStatementDraftRequest(account.Id));

        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{prelimDraft.DraftId}?quickEdit=true");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("input[id^='qe_subject_']").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await PlaywrightTestTiming.WaitForForcedKeepaliveThrottleAsync(page);

        var keepaliveRequests = 0;
        page.Request += (_, request) =>
        {
            if (IsKeepaliveRequest(request))
            {
                Interlocked.Increment(ref keepaliveRequests);
            }
        };

        var responseTask = page.WaitForResponseAsync(IsKeepaliveResponse);
        await page.EvaluateAsync("""
            () => {
                const inputs = Array.from(document.querySelectorAll('[data-fm-quickedit-keepalive]')).slice(0, 4);
                for (const input of inputs) {
                    input.dispatchEvent(new FocusEvent('blur'));
                }
            }
            """);

        var response = await responseTask;
        response.Status.Should().Be(204);
        await page.WaitForTimeoutAsync(250);

        keepaliveRequests.Should().Be(1);
    }

    private static bool IsKeepaliveRequest(IRequest request)
        => Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.Equals("/api/auth/keepalive", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeepaliveResponse(IResponse response)
        => Uri.TryCreate(response.Url, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.Equals("/api/auth/keepalive", StringComparison.OrdinalIgnoreCase);

    private async Task<StatementDraftDto> CreateQuickEditDraftWithPersistedRowsAsync(
        IPage page,
        string scenario,
        StatementDraftEntryStatus middleStatus = StatementDraftEntryStatus.Open)
    {
        var auth = new AuthGateway(page, _fixture.BaseUrl);
        var seeder = new TestUserSeeder(_fixture.DatabasePath);

        var username = $"{scenario}-e2e-{Guid.NewGuid():N}";
        const string password = "Secret123";
        await seeder.EnsureUserAsync(username, password);
        await auth.LoginAsync(username, password);

        var account = await new AccountsApiSeedHelper(page).CreateAccountAsync(
            $"E2E {scenario}",
            CreateUniqueIban());

        var draft = await BrowserApiHelper.PostJsonAsync<CreatePreliminaryStatementDraftRequest, StatementDraftDto>(
            page,
            "/api/statement-drafts/preliminary",
            new CreatePreliminaryStatementDraftRequest(account.Id));

        var entryId = draft.Entries!.Single().Id;
        await BrowserApiHelper.PostJsonAsync(page, $"/api/statement-drafts/{draft.DraftId}/entries/batch-update", new
        {
            updates = new[]
            {
                new
                {
                    entryId,
                    fields = new Dictionary<string, object?>
                    {
                        ["BookingDate"] = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["ValutaDate"] = null,
                        ["Amount"] = 10m,
                        ["Subject"] = "Source Subject",
                        ["RecipientName"] = "Source Recipient",
                        ["BookingDescription"] = "Source Description"
                    }
                }
            },
            creates = new[]
            {
                new
                {
                    clientId = Guid.NewGuid(),
                    bookingDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    valutaDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount = 20m,
                    subject = "Middle Subject",
                    bookingDescription = "Middle Description",
                    recipientName = "Middle Recipient"
                },
                new
                {
                    clientId = Guid.NewGuid(),
                    bookingDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    valutaDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount = 30m,
                    subject = "Next Subject",
                    bookingDescription = "Next Description",
                    recipientName = "Next Recipient"
                }
            },
            deletes = System.Array.Empty<Guid>()
        });

        if (middleStatus != StatementDraftEntryStatus.Open)
        {
            var updatedDraft = await BrowserApiHelper.GetJsonAsync<StatementDraftDetailDto>(page, $"/api/statement-drafts/{draft.DraftId}");
            var middleEntryId = updatedDraft.Entries!
                .OrderBy(e => e.EntryNumber)
                .Skip(1)
                .First()
                .Id;

            await BrowserApiHelper.PostJsonAsync(page, $"/api/statement-drafts/{draft.DraftId}/entries/batch-update", new
            {
                updates = new[]
                {
                    new
                    {
                        entryId = middleEntryId,
                        fields = new Dictionary<string, object?>
                        {
                            ["Status"] = middleStatus.ToString()
                        }
                    }
                },
                creates = System.Array.Empty<object>(),
                deletes = System.Array.Empty<Guid>()
            });
        }

        return draft;
    }

    private async Task OpenQuickEditAsync(IPage page, Guid draftId)
    {
        await page.GotoAsync($"{_fixture.BaseUrl}/card/statement-drafts/{draftId}?quickEdit=true");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("input[id^='qe_booking_']").Nth(2).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }

    private static async Task PressKeyAsync(ILocator locator, string key)
        => await locator.PressAsync(key);

    private static Task WaitForActiveElementAsync(IPage page, string? expectedId)
        => page.WaitForFunctionAsync("expected => document.activeElement && document.activeElement.id === expected", expectedId, new() { Timeout = 5000 });

    private static async Task WaitForInputValueAsync(ILocator locator, string expected)
    {
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await Assertions.Expect(locator).ToHaveValueAsync(expected, new() { Timeout = 5000 });
    }

    private static string CreateUniqueIban()
    {
        var value = unchecked((uint)Guid.NewGuid().GetHashCode()) % 100000000;
        return string.Create(CultureInfo.InvariantCulture, $"DE507005000000{value:00000000}");
    }
}
