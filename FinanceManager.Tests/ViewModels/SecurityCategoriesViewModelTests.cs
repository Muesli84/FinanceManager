using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SecurityCategoriesViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (SecurityCategoriesViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SecurityCategoriesViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Loads_Categories()
    {
        var (vm, apiMock) = CreateVm();
        var categories = new List<SecurityCategoryDto>
        {
            new SecurityCategoryDto { Id = Guid.NewGuid(), Name = "A" },
            new SecurityCategoryDto { Id = Guid.NewGuid(), Name = "B" }
        };
        apiMock.Setup(a => a.SecurityCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal(new[] { "A", "B" }, vm.Categories.Select(x => x.Name).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Initialize_RequiresAuth_When_NotAuthenticated()
    {
        var (vm, apiMock) = CreateVm(authenticated: false);
        bool authRequired = false;
        vm.AuthenticationRequired += (_, __) => authRequired = true;

        await vm.InitializeAsync();

        Assert.False(vm.Loaded);
        Assert.True(authRequired);
        apiMock.Verify(a => a.SecurityCategories_ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Ribbon_Has_Actions()
    {
        var (vm, _) = CreateVm();
        var loc = new TestLocalizer<SecurityCategoriesViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var actions = groups.First();
        Assert.Contains(actions.Items, i => i.Action == "New");
        Assert.Contains(actions.Items, i => i.Action == "Back");
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
    }
}
