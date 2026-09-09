using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Shared.Dtos.Statements;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

using ApiClientsPair = (FinanceManager.Shared.ApiClient, System.Net.Http.HttpClient);

namespace FinanceManager.Tests.Integration.ApiClient;

/// <summary>
/// Integration tests for the posting reversal API endpoints.
/// Tests cover the full HTTP stack including authentication, authorization, and business rules.
/// Each test creates an independent user and account to avoid cross-test state leakage.
/// </summary>
public sealed class ApiClientPostingsReversalTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>Initializes the test with the shared in-memory web application factory.</summary>
    /// <param name="factory">The shared in-memory test host used to spin up API clients.</param>
    public ApiClientPostingsReversalTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a typed API client together with the underlying raw HttpClient
    /// so tests can inspect HTTP status codes directly when needed.
    /// </summary>
    /// <returns>The typed API client and the underlying HTTP client.</returns>
    private ApiClientsPair CreateClients()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return (new FinanceManager.Shared.ApiClient(http), http);
    }

    private static string NewUniqueIban()
    {
        // Generate a syntactically plausible German IBAN-like string that is unique per test.
        // IBAN validation is not enforced by the application; uniqueness is the only requirement.
        var digits = Math.Abs(Guid.NewGuid().GetHashCode()).ToString("D18").Substring(0, 18);
        return $"DE50{digits}";
    }

    /// <summary>
    /// Registers a fresh user (and auto-logs in via cookie) using a unique username.
    /// </summary>
    /// <param name="api">The API client used for registration.</param>
    /// <returns>The result.</returns>
    private static async Task<string> RegisterUserAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"rev_user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
        return username;
    }

    /// <summary>
    /// Runs the full statement-import flow to produce a single booked posting.
    /// Returns the account id and the id of the first created posting.
    /// </summary>
    /// <param name="api">The API client used for the statement flow.</param>
    /// <returns>The result.</returns>
    private static async Task<(Guid accountId, Guid postingId)> BookPostingViaStatementAsync(
        FinanceManager.Shared.ApiClient api)
    {
        var iban = NewUniqueIban();

        // Create an account with the unique IBAN
        var acc = await api.CreateAccountAsync(new AccountCreateRequest(
            Name: "Reversal Test Account",
            Type: AccountType.Giro,
            Iban: iban,
            BankContactId: null,
            NewBankContactName: "Test Bank",
            SymbolAttachmentId: null,
            SavingsPlanExpectation: SavingsPlanExpectation.Optional,
            SecurityProcessingEnabled: true));
        acc.Should().NotBeNull();

        // Build an ING-format CSV for the account's IBAN
        var csv =
            "Umsatzanzeige;Datei erstellt am: 02.12.2025 19:04\r\n" +
            "\r\n" +
            $"IBAN;{iban}\r\n" +
            "Kontoname;Girokonto\r\n" +
            "Bank;ING\r\n" +
            "Kunde;TestUser\r\n" +
            "Zeitraum;02.11.2025 - 02.12.2025\r\n" +
            "Saldo;1.000,00;EUR\r\n" +
            "\r\n" +
            "Sortierung;Datum absteigend\r\n" +
            "\r\n" +
            "\r\n" +
            "Buchung;Wertstellungsdatum;Auftraggeber/Empfänger;Buchungstext;Verwendungszweck;Saldo;Währung;Betrag;Währung\r\n" +
            "02.12.2025;02.12.2025;Testempfänger;Überweisung;Reversal Test;1.000,00;EUR;-100,00;EUR\r\n";

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var upload = await api.StatementDrafts_UploadAsync(ms, "reversal_test.csv");
        upload.Should().NotBeNull();
        var draft = upload!.FirstDraft;
        draft.Should().NotBeNull();

        // Assign a contact to satisfy the booking validation
        var contact = await api.Contacts_CreateAsync(new ContactCreateRequest(
            Name: $"Rev Contact {Guid.NewGuid():N}",
            Description: null,
            Type: ContactType.Other,
            CategoryId: null,
            IsPaymentIntermediary: false));
        contact.Should().NotBeNull();

        var draftDetail = await api.StatementDrafts_GetAsync(draft!.DraftId);
        draftDetail.Should().NotBeNull();
        var firstEntryId = draftDetail!.Entries.First().Id;

        await api.StatementDrafts_SetEntryContactAsync(
            draft.DraftId, firstEntryId,
            new StatementDraftSetContactRequest(contact!.Id));

        var bookResult = await api.StatementDrafts_BookAsync(draft.DraftId, forceWarnings: true);
        bookResult.Should().NotBeNull();
        bookResult!.Success.Should().BeTrue("booking must succeed to obtain a posting for reversal tests");

        // Retrieve the created posting from the account
        var postings = await api.Postings_GetAccountAsync(acc.Id, 0, 10);
        postings.Should().NotBeNullOrEmpty("at least one posting must exist after booking");
        var postingId = postings.First().Id;

        return (acc.Id, postingId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// L01 – Happy path: authenticated owner reverses a posting via POST /api/postings/{id}/reverse.
    /// Expects HTTP 200 with a ReversalResultDto containing valid ids.
    /// </summary>
    [Fact]
    public async Task ReversePosting_ShouldReturn200_WithReversalResult_ForOwner()
    {
        // Arrange
        var (api, _) = CreateClients();
        await RegisterUserAsync(api);
        var (_, postingId) = await BookPostingViaStatementAsync(api);

        // Act
        var result = await api.Postings_ReverseAsync(postingId, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull("reverse must succeed for the posting owner");
        result!.ReversedPostingIds.Should().Contain(postingId);
        result.CreatedReversalIds.Should().NotBeEmpty();
        result.StatementDraftId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// L02 – Idempotency guard: reversing an already-reversed posting returns HTTP 409 Conflict.
    /// </summary>
    [Fact]
    public async Task ReversePosting_ShouldReturn409_WhenAlreadyReversed()
    {
        // Arrange
        var (api, http) = CreateClients();
        await RegisterUserAsync(api);
        var (_, postingId) = await BookPostingViaStatementAsync(api);

        // First reversal must succeed
        var first = await api.Postings_ReverseAsync(postingId, TestContext.Current.CancellationToken);
        first.Should().NotBeNull("first reversal must succeed");

        // Act – second reversal on the same posting
        var response = await http.PostAsync($"/api/postings/{postingId}/reverse", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "reversing an already-reversed posting must result in 409 Conflict");
    }

    /// <summary>
    /// L03 – Cross-user reversal attempt: user B cannot reverse a posting owned by user A.
    /// NOTE: The controller documents HTTP 403 for this case, but the service actually throws
    /// InvalidOperationException (not UnauthorizedAccessException), so the actual response is
    /// HTTP 400 Bad Request. This is a known discrepancy; the test asserts the actual behaviour.
    /// See PostingsController.ReversePosting and PostingReversalService.ReversePostingAsync.
    /// </summary>
    [Fact]
    public async Task ReversePosting_ShouldReturn400_WhenUserIsNotOwner()
    {
        // Arrange – user A creates a posting
        var (apiA, _) = CreateClients();
        await RegisterUserAsync(apiA);
        var (_, postingId) = await BookPostingViaStatementAsync(apiA);

        // User B (different client = independent cookie/session)
        var (_, httpB) = CreateClients();
        // Register user B so the request is authenticated (just not as owner of the account)
        var apiBWrapper = new FinanceManager.Shared.ApiClient(httpB);
        await RegisterUserAsync(apiBWrapper);

        // Act – user B tries to reverse user A's posting
        var response = await httpB.PostAsync($"/api/postings/{postingId}/reverse", null, TestContext.Current.CancellationToken);

        // Assert – HTTP 400 is the actual response (InvalidOperationException mapped to 400)
        // The controller would return 403 only if the service threw UnauthorizedAccessException,
        // which it currently does not for ownership violations.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ownership violation currently maps to 400 (InvalidOperationException path in controller)");
    }

    /// <summary>
    /// L04 – Reversing a non-existent posting id returns HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task ReversePosting_ShouldReturn400_WhenPostingNotFound()
    {
        // Arrange
        var (api, http) = CreateClients();
        await RegisterUserAsync(api);
        var nonExistentId = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-000000000001");

        // Act
        var response = await http.PostAsync($"/api/postings/{nonExistentId}/reverse", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a non-existent posting id results in 400 Bad Request");
    }

    /// <summary>
    /// L05 – GET /api/postings/{id}/validate-reversal returns HTTP 200 with IsValid = true
    /// for a posting that can be reversed by its owner.
    /// </summary>
    [Fact]
    public async Task ValidateReversal_ShouldReturn200WithIsValidTrue_ForReversiblePosting()
    {
        // Arrange
        var (api, _) = CreateClients();
        await RegisterUserAsync(api);
        var (_, postingId) = await BookPostingViaStatementAsync(api);

        // Act
        var validation = await api.Postings_ValidateReversalAsync(postingId, TestContext.Current.CancellationToken);

        // Assert
        validation.Should().NotBeNull();
        validation!.IsValid.Should().BeTrue("the owner should be able to reverse a fresh posting");
        validation.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// L06 – Unauthenticated call to POST /api/postings/{id}/reverse returns HTTP 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task ReversePosting_ShouldReturn401_WhenNotAuthenticated()
    {
        // Arrange – create HTTP client but do NOT authenticate
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var anyId = new Guid("AAAAAAAA-0000-0000-0000-000000000001");

        // Act
        var response = await http.PostAsync($"/api/postings/{anyId}/reverse", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated requests must be rejected with 401");
    }
}
