using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class ReportsHomeViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (ReportsHomeViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new ReportsHomeViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_LoadsFavorites_SortsByName()
    {
        var (vm, apiMock) = CreateVm();
        var favorites = Enumerable.Range(0, 3)
            .Select(i => new ReportFavoriteDto(
                Guid.NewGuid(),
                $"Fav {3 - i}",
                PostingKind.Bank,
                false,
                0,
                24,
                false,
                false,
                true,
                true,
                DateTime.UtcNow.AddDays(-i),
                null,
                new[] { PostingKind.Bank },
                null,
                false))
            .ToList();

        apiMock.Setup(a => a.Reports_ListFavoritesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(favorites);

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal(3, vm.Favorites.Count);
        Assert.Collection(vm.Favorites,
            a => Assert.Equal("Fav 1", a.Name),
            b => Assert.Equal("Fav 2", b.Name),
            c => Assert.Equal("Fav 3", c.Name));
    }

    [Fact]
    public async Task Reload_DoesNotThrow_OnError()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Reports_ListFavoritesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Error"));

        await vm.InitializeAsync();
        await vm.ReloadAsync();

        Assert.False(vm.Loading);
    }
}
