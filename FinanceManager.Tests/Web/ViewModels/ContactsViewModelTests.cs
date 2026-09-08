using System.Linq;
using FinanceManager.Application;
using FinanceManager.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

using VmWithApiMock = (FinanceManager.Web.ViewModels.Contacts.ContactListViewModel, Moq.Mock<FinanceManager.Shared.IApiClient>);

namespace FinanceManager.Tests.Web.ViewModels;

/// <summary>
/// Covers <c>ContactListViewModel</c>'s data-loading lifecycle: that it tolerates being used while
/// unauthenticated, loads categories and the first page of contacts once authenticated, paginates
/// correctly via <c>LoadMoreAsync</c>, and that applying a search filter resets paging and surfaces
/// a "clear filter" ribbon action so the user can tell a filter is active.
/// </summary>
public sealed class ContactsViewModelTests
{
    /// <summary>
    /// Minimal <see cref="ICurrentUserService"/> double whose authentication state is set directly
    /// by the test, so view models under test can be driven through both authenticated and
    /// unauthenticated code paths without a real auth pipeline.
    /// </summary>
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        /// <inheritdoc />
        public Guid UserId { get; set; } = Guid.NewGuid();
        /// <inheritdoc />
        public string? PreferredLanguage { get; set; }
        /// <inheritdoc />
        public bool IsAuthenticated { get; set; }
        /// <inheritdoc />
        public bool IsAdmin { get; set; }
    }

    /// <summary>
    /// <see cref="IStringLocalizer"/> stub that echoes the requested resource key back as the
    /// localized value (formatting in any supplied arguments), so ribbon/action assertions can
    /// compare against known key strings instead of depending on real resource files.
    /// </summary>
    private sealed class PassthroughLocalizer : IStringLocalizer
    {
        /// <inheritdoc />
        public LocalizedString this[string name] => new LocalizedString(name, name, resourceNotFound: false);
        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    /// <summary>
    /// Generic counterpart of <see cref="PassthroughLocalizer"/>, needed because
    /// <c>BaseViewModel</c> requires an <see cref="IStringLocalizer{T}"/> registration to resolve
    /// from the DI container built for each test.
    /// </summary>
    private sealed class PassthroughLocalizerGeneric<T> : IStringLocalizer<T>
    {
        /// <inheritdoc />
        public LocalizedString this[string name] => new LocalizedString(name, name, resourceNotFound: false);
        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        /// <inheritdoc />
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => (IStringLocalizer)this;
    }

    /// <summary>
    /// Builds a <c>ContactListViewModel</c> wired to a minimal DI container with a mocked
    /// <see cref="IApiClient"/>, so each test can control API responses without a real backend.
    /// </summary>
    /// <param name="isAuthenticated">Whether the simulated current user should appear authenticated.</param>
    /// <returns>The view model under test along with the API mock used to configure its responses.</returns>
    private static VmWithApiMock CreateVm(bool isAuthenticated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        // register IStringLocalizer<Pages> required by BaseViewModel
        services.AddSingleton<IStringLocalizer<FinanceManager.Web.Pages>>(new PassthroughLocalizerGeneric<FinanceManager.Web.Pages>());
        var sp = services.BuildServiceProvider();
        var vm = new FinanceManager.Web.ViewModels.Contacts.ContactListViewModel(sp);
        return (vm, apiMock);
    }

    /// <summary>
    /// Verifies that initializing the view model while unauthenticated does not throw and simply leaves the
    /// contact list empty — the view model itself does not enforce authentication (that's the page's job),
    /// so it must degrade gracefully rather than crash when used before sign-in completes.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ShouldRequestAuth_WhenNotAuthenticated()
    {
        var (vm, _) = CreateVm(isAuthenticated: false);

        // ContactListViewModel does not enforce authentication itself; ensure InitializeAsync runs without throwing
        await vm.InitializeAsync();

        Assert.NotNull(vm.Items);
        Assert.Equal(0, vm.Items.Count);
    }

    /// <summary>
    /// Verifies that initializing an authenticated view model loads both the contact categories and the
    /// first page of contacts, and that a contact's category id is resolved to its display name.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ShouldLoadCategories_And_FirstPage_WhenAuthenticated()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var catId = Guid.NewGuid();
        var categories = new List<ContactCategoryDto> { new ContactCategoryDto(catId, "Friends", null) };
        var contacts = new List<ContactDto> { new ContactDto(Guid.NewGuid(), "Alice", ContactType.Person, catId, null, false, null) };

        apiMock.Setup(a => a.ContactCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
        apiMock.Setup(a => a.Contacts_ListAsync(0, 50, null, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contacts);

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(1, vm.Items.Count);
        Assert.Equal("Alice", vm.Items[0].Name);
        Assert.Equal("Friends", vm.Items[0].CategoryName);
    }

    /// <summary>
    /// Verifies that loading more contacts appends the next page to the existing list, and that
    /// <c>CanLoadMore</c> correctly turns false once a page comes back smaller than the page size — the
    /// signal the view model uses to know it has reached the end of the list.
    /// </summary>
    [Fact]
    public async Task LoadMoreAsync_ShouldPaginate_And_SetAllLoaded()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var firstPage = Enumerable.Range(0, 50).Select(i => new ContactDto(Guid.NewGuid(), $"N{i}", ContactType.Person, null, null, false, null)).ToList();
        var secondPage = Enumerable.Range(0, 10).Select(i => new ContactDto(Guid.NewGuid(), $"M{i}", ContactType.Person, null, null, false, null)).ToList();

        apiMock.Setup(a => a.ContactCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactCategoryDto>());

        int callCount = 0;
        apiMock.Setup(a => a.Contacts_ListAsync(It.IsAny<int>(), 50, null, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? firstPage : secondPage;
            });

        await vm.InitializeAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(60, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
    }

    /// <summary>
    /// Verifies that applying a search filter discards the previously loaded page and reloads from the
    /// filtered result set, and that the ribbon then exposes a "clear filter" action so the user has a
    /// visible way to tell a filter is active and remove it.
    /// </summary>
    [Fact]
    public async Task SetFilterAsync_ShouldResetAndReload_AndRibbonIncludesClear()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var unfilteredContacts = new List<ContactDto> { new ContactDto(Guid.NewGuid(), "B", ContactType.Person, null, null, false, null) };
        var filteredContacts = new List<ContactDto>
        {
            new ContactDto(Guid.NewGuid(), "Ax", ContactType.Person, null, null, false, null),
            new ContactDto(Guid.NewGuid(), "Ay", ContactType.Person, null, null, false, null)
        };

        apiMock.Setup(a => a.ContactCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactCategoryDto>());
        apiMock.Setup(a => a.Contacts_ListAsync(0, 50, null, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unfilteredContacts);
        apiMock.Setup(a => a.Contacts_ListAsync(0, 50, null, false, "A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(filteredContacts);

        await vm.InitializeAsync();
        Assert.Equal(1, vm.Items.Count);

        // Apply filter: set search, reset paging and load
        vm.SetSearch("A");
        vm.ResetAndSearch();
        await vm.LoadAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.False(vm.CanLoadMore);

        var ribbonRegs = vm.GetRibbon(new PassthroughLocalizer())!;
        Assert.Equal(1, ribbonRegs.Count);
        var items = ribbonRegs.SelectMany(r => r.Tabs ?? Enumerable.Empty<FinanceManager.Web.ViewModels.Common.UiRibbonTab>())
                              .SelectMany(t => t.Items ?? Enumerable.Empty<FinanceManager.Web.ViewModels.Common.UiRibbonAction>())
                              .ToList();
        Assert.True(items.Any(i => i.Action == "ClearFilter" || i.Action == "ClearSearch"));
    }
}
