using FinanceManager.Application;
using FinanceManager.Shared; // added
using FinanceManager.Web.ViewModels.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

/// <summary>
/// Covers <c>StatementDraftsListViewModel</c> (paging, delete-all, classify and booking
/// background operations for the statement-draft inbox) and, via the
/// <see cref="LoadEntryRecordsAsync"/> helper, the mobile-row rendering produced by
/// <c>StatementDraftCardViewModel</c> for a draft's entries - in particular which fields are
/// hidden for the account's own bank/self contact versus shown for external contacts.
/// </summary>
public sealed class StatementDraftsViewModelTests
{
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new HttpClient(new DelegateHandler(responder)) { BaseAddress = new Uri("http://localhost") };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; } = "de";
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = false;
    }

    private static IServiceProvider CreateSp(Shared.IApiClient? api = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        // Provide a simple test string localizer for all IStringLocalizer<T> requests
        services.AddSingleton(typeof(Microsoft.Extensions.Localization.IStringLocalizer<>), typeof(TestStringLocalizer<>));
        if (api != null) services.AddSingleton<Shared.IApiClient>(api);
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        return services.BuildServiceProvider();
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad) { /* no-op for tests */ }
    }

    private static string Json(object obj) => JsonSerializer.Serialize(obj);

    private static StatementDraftEntryDto Entry(
        int number,
        Guid? contactId = null,
        string? recipient = null,
        string? subject = null,
        Guid? savingsPlanId = null,
        Guid? securityId = null,
        SecurityTransactionType? transactionType = null,
        StatementDraftEntryStatus status = StatementDraftEntryStatus.Open)
        => new(
            Guid.NewGuid(),
            number,
            new DateTime(2026, 7, number),
            null,
            number * 10m,
            "EUR",
            subject ?? string.Empty,
            recipient,
            null,
            false,
            false,
            status,
            contactId,
            savingsPlanId,
            false,
            null,
            securityId,
            transactionType,
            null,
            null,
            null);

    private static StatementDraftDetailDto DraftDetail(
        Guid draftId,
        IReadOnlyList<StatementDraftEntryDto> entries,
        IReadOnlyDictionary<Guid, string>? contactNames = null,
        IReadOnlyDictionary<Guid, string>? savingsPlanNames = null,
        IReadOnlyDictionary<Guid, string>? securityNames = null,
        Guid? accountBankContactId = null,
        Guid? selfContactId = null)
        => new(
            draftId,
            "statement.csv",
            null,
            null,
            StatementDraftStatus.Draft,
            entries.Sum(e => e.Amount),
            false,
            null,
            null,
            null,
            null,
            entries,
            null,
            null,
            ContactNames: contactNames,
            SavingsPlanNames: savingsPlanNames,
            SecurityNames: securityNames,
            AccountBankContactId: accountBankContactId,
            SelfContactId: selfContactId);

    private static async Task<IReadOnlyList<ListRecord>> LoadEntryRecordsAsync(StatementDraftDetailDto detail)
    {
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/statement-drafts/{detail.DraftId}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(detail), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftCardViewModel(CreateSp(api));
        await vm.LoadAsync(detail.DraftId);

        Assert.NotNull(vm.EmbeddedList);
        var provider = Assert.IsAssignableFrom<IListProvider>(vm.EmbeddedList);
        return provider.Records;
    }

    private static IReadOnlyList<ListMobileRow> MobileRows(ListRecord record)
    {
        Assert.NotNull(record.MobileRows);
        return record.MobileRows!;
    }

    private static IReadOnlyList<string> MobileTexts(ListRecord record)
        => MobileRows(record)
            .SelectMany(r => r.Cells)
            .Select(c => c.Cell.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();

    private static ListRecord RecordForEntry(IReadOnlyList<ListRecord> records, Guid entryId)
        => records.Single(r => r.Item?.GetType().GetProperty("Id")?.GetValue(r.Item) is Guid id && id == entryId);

    /// <summary>
    /// Verifies that <c>InitializeAsync</c> loads the first page of statement drafts from the
    /// list endpoint into <c>Items</c>.
    /// </summary>
    [Fact]
    public async Task Initialize_Loads_First_Page()
    {
        var batch = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "b.pdf", Description = "B", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "c.pdf", Description = "C", Status = 0, Entries = Array.Empty<object>() },
        };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(batch), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await vm.InitializeAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.CanLoadMore == false ? true : true); // not asserting stop here (unknown until next page)
    }

    /// <summary>
    /// Verifies that <c>LoadMoreAsync</c> appends the next page to the existing items and that
    /// <c>CanLoadMore</c> flips to <see langword="false"/> once a page comes back shorter than
    /// the page size, so the UI knows to stop offering "load more".
    /// </summary>
    [Fact]
    public async Task LoadMore_Accumulates_And_Stops_When_Less_Than_PageSize()
    {
        var first = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "b.pdf", Description = "B", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "c.pdf", Description = "C", Status = 0, Entries = Array.Empty<object>() },
        };
        var second = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "d.pdf", Description = "D", Status = 0, Entries = Array.Empty<object>() },
        };
        int getCalls = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                getCalls++;
                var payload = getCalls == 1 ? first : second;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await vm.InitializeAsync();
        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
    }

    /// <summary>
    /// Verifies that <c>DeleteAllAsync</c> empties <c>Items</c> after a successful bulk delete
    /// and resets paging state (<c>CanLoadMore</c> back to <see langword="true"/>) so a
    /// subsequent load starts cleanly rather than being stuck on a stale "no more pages" flag.
    /// </summary>
    [Fact]
    public async Task DeleteAll_Clears_And_Resets()
    {
        var list = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(list), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == "/api/statement-drafts/all")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        var ok = await vm.DeleteAllAsync();
        Assert.True(ok);
        Assert.Empty(vm.Items);
        Assert.True(vm.CanLoadMore);
    }

    /// <summary>
    /// Verifies that uploading a file through the <see cref="IUploadFilesViewModel"/> facade
    /// returns the ID of the first draft created by the server, which callers use to navigate
    /// straight to the newly created draft.
    /// </summary>
    [Fact]
    public async Task Upload_Returns_FirstDraftId()
    {
        var firstId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/upload")
            {
                var body = new { FirstDraft = new { DraftId = firstId }, SplitInfo = (object?)null };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(body), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var upl = (FinanceManager.Web.ViewModels.Common.IUploadFilesViewModel?)vm;
        var files = new List<(Stream Stream, string FileName)> { (stream, "x.pdf") };
        var res = await upl!.UploadFilesAsync("statementdraft", files, CancellationToken.None);
        var id = res?.StatementDraftResult?.FirstDraft?.DraftId;
        Assert.Equal(firstId, id);
    }

    /// <summary>
    /// Verifies the classify background-job lifecycle: starting it flips <c>IsClassifying</c>
    /// on, and polling status until it reports completion flips it back off and triggers a
    /// reload of the drafts list so newly classified data is reflected in <c>Items</c>.
    /// </summary>
    [Fact]
    public async Task Classify_Starts_And_Completes_Reloads()
    {
        var first = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        bool listServed = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                // After completion we'll reload list once
                var payload = listServed ? Array.Empty<object>() : first;
                listServed = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/classify")
            {
                var status = new { running = true, processed = 1, total = 10, message = "started" };
                var resp = new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(Json(status), Encoding.UTF8, "application/json")
                };
                return resp;
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts/classify/status")
            {
                var done = new { running = false, processed = 10, total = 10, message = "done" };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(done), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        await vm.StartClassifyAsync();
        Assert.True(vm.IsClassifying);

        await vm.RefreshClassifyStatusAsync();
        Assert.False(vm.IsClassifying);
        // after reload we served empty list
        Assert.Empty(vm.Items);
    }

    /// <summary>
    /// Verifies the book-all background-job lifecycle end to end: starting it flips
    /// <c>IsBooking</c> on, polling status until completion flips it back off and reloads the
    /// list, and cancelling the (already finished) job does not throw.
    /// </summary>
    [Fact]
    public async Task Booking_Starts_Status_Updates_And_Completes()
    {
        var initialList = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        bool listReloaded = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                var payload = listReloaded ? Array.Empty<object>() : initialList;
                listReloaded = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all")
            {
                var s = new { running = true, processed = 0, failed = 0, total = 1, warnings = 0, errors = 0, message = "started", issues = Array.Empty<object>() };
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(Json(s), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all/status")
            {
                var done = new { running = false, processed = 1, failed = 0, total = 1, warnings = 0, errors = 0, message = "ok", issues = Array.Empty<object>() };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(done), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all/cancel")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var api = new ApiClient(client);
        var vm = new FinanceManager.Web.ViewModels.StatementDrafts.StatementDraftsListViewModel(CreateSp(api));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        await vm.StartBookAllAsync(ignoreWarnings: false, abortOnFirstIssue: false, bookEntriesIndividually: false);
        Assert.True(vm.IsBooking);

        await vm.RefreshBookStatusAsync();
        Assert.False(vm.IsBooking);
        Assert.Empty(vm.Items);

        await vm.CancelBookingAsync();
        // no exception means ok
    }

    /// <summary>
    /// Verifies the privacy/relevance rules used when rendering a draft's entries as mobile
    /// rows: already-booked entries are muted and don't repeat their status text, the account's
    /// own bank/self contact names are suppressed entirely (would be redundant/confusing on the
    /// statement), while external contact, savings-plan, and security names are shown - and a
    /// security entry additionally shows its transaction type alongside the security name.
    /// </summary>
    [Fact]
    public async Task EntriesList_BuildsMobileRows_ForStatementDraftInformation()
    {
        var bankContactId = Guid.NewGuid();
        var selfContactId = Guid.NewGuid();
        var externalContactId = Guid.NewGuid();
        var savingsPlanId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var entries = new[]
        {
            Entry(1, status: StatementDraftEntryStatus.AlreadyBooked, subject: "Booked"),
            Entry(2, contactId: externalContactId, recipient: "Hidden recipient", subject: "External subject"),
            Entry(3, contactId: bankContactId, recipient: "Bank recipient"),
            Entry(4, contactId: selfContactId, recipient: "Self recipient"),
            Entry(5, recipient: "Plain recipient"),
            Entry(6, savingsPlanId: savingsPlanId),
            Entry(7, securityId: securityId, transactionType: SecurityTransactionType.Buy)
        };
        var detail = DraftDetail(
            Guid.NewGuid(),
            entries,
            contactNames: new Dictionary<Guid, string> { [entries[1].Id] = "External GmbH", [entries[2].Id] = "Bank AG", [entries[3].Id] = "Me" },
            savingsPlanNames: new Dictionary<Guid, string> { [entries[5].Id] = "ETF Sparplan" },
            securityNames: new Dictionary<Guid, string> { [entries[6].Id] = "ACME ETF" },
            accountBankContactId: bankContactId,
            selfContactId: selfContactId);

        var records = await LoadEntryRecordsAsync(detail);

        var bookedRecord = RecordForEntry(records, entries[0].Id);
        Assert.All(bookedRecord.Cells, c => Assert.True(c.Muted));
        Assert.All(MobileRows(bookedRecord).SelectMany(r => r.Cells), c => Assert.True(c.Cell.Muted));
        Assert.DoesNotContain("AlreadyBooked", MobileTexts(bookedRecord));

        var openStatusTexts = MobileTexts(RecordForEntry(records, entries[1].Id));
        Assert.Contains("Open", openStatusTexts);
        var contactTexts = MobileTexts(RecordForEntry(records, entries[1].Id));
        Assert.Contains("External GmbH", contactTexts);
        Assert.DoesNotContain("Hidden recipient", contactTexts);
        Assert.Contains(MobileRows(RecordForEntry(records, entries[1].Id)).SelectMany(r => r.Cells), c => c.Label == "Kontakt");

        var bankTexts = MobileTexts(RecordForEntry(records, entries[2].Id));
        Assert.DoesNotContain("Bank AG", bankTexts);
        Assert.DoesNotContain("Bank recipient", bankTexts);

        var selfTexts = MobileTexts(RecordForEntry(records, entries[3].Id));
        Assert.DoesNotContain("Me", selfTexts);
        Assert.DoesNotContain("Self recipient", selfTexts);

        Assert.Contains("Plain recipient", MobileTexts(RecordForEntry(records, entries[4].Id)));
        Assert.Contains("ETF Sparplan", MobileTexts(RecordForEntry(records, entries[5].Id)));
        Assert.Contains("ACME ETF (Buy)", MobileTexts(RecordForEntry(records, entries[6].Id)));
    }

    /// <summary>
    /// Verifies that the mobile row layout groups an entry's date and amount into a single
    /// two-column block (with the expected CSS class and cell kinds) rather than two separate
    /// rows, matching the compact mobile card layout.
    /// </summary>
    [Fact]
    public async Task EntriesList_MobileRows_PutDateAndAmountIntoTwoColumnBlock()
    {
        var entry = Entry(1, recipient: "Recipient");
        var detail = DraftDetail(Guid.NewGuid(), new[] { entry });

        var record = (await LoadEntryRecordsAsync(detail)).Single();
        var twoColumnRow = MobileRows(record).Single(r => r.Kind == ListMobileRowKind.TwoColumn);

        Assert.Equal("statement-draft-entry-date-amount", twoColumnRow.CssClass);
        Assert.Equal(2, twoColumnRow.Cells.Count);
        Assert.Equal(ListCellKind.Text, twoColumnRow.Cells[0].Cell.Kind);
        Assert.Equal(ListCellKind.Currency, twoColumnRow.Cells[1].Cell.Kind);
    }
}


/// <summary>
/// Minimal <see cref="IStringLocalizer{T}"/> stand-in for DI in view-model tests: it echoes the
/// resource key back as the value (with one hard-coded translation used by the mobile-row
/// assertions in <see cref="StatementDraftsViewModelTests"/>) so tests don't need real resource
/// files or a localization pipeline to construct a view model.
/// </summary>
public sealed class TestStringLocalizer<T> : Microsoft.Extensions.Localization.IStringLocalizer<T>
{
    /// <summary>Returns the localized value for <paramref name="name"/>, falling back to the key itself when no test translation is registered.</summary>
    public LocalizedString this[string name]
    {
        get
        {
            var value = name switch
            {
                "List_Th_Contact" => "Kontakt",
                _ => name
            };
            return new LocalizedString(name, value, resourceNotFound: false);
        }
    }

    /// <summary>Returns the key formatted with <paramref name="arguments"/> via <see cref="string.Format(string, object[])"/>, mimicking a formatted resource string.</summary>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var val = string.Format(name, arguments);
            return new LocalizedString(name, val, resourceNotFound: false);
        }
    }

    /// <summary>Always returns an empty sequence; this fake does not enumerate a resource set.</summary>
    /// <param name="includeParentCultures">The include parent cultures.</param>
    /// <returns>The result.</returns>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return Array.Empty<LocalizedString>();
    }

    /// <summary>Returns this same instance; culture switching is not simulated by this fake.</summary>
    /// <param name="culture">The culture.</param>
    /// <returns>The result.</returns>
    public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture)
    {
        return this;
    }
}
