using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Security;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupIpBlocksViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = true;
    }

    private static (SetupIpBlocksViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SetupIpBlocksViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Loads_List_When_Admin()
    {
        var (vm, apiMock) = CreateVm();
        IReadOnlyList<IpBlockDto> items = new List<IpBlockDto>
        {
            new IpBlockDto(Guid.NewGuid(), "1.2.3.4", false, null, null, 0, null, DateTime.UtcNow, null)
        };

        apiMock.Setup(a => a.Admin_ListIpBlocksAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        await vm.InitializeAsync();

        Assert.Single(vm.Items);
        Assert.Equal("1.2.3.4", vm.Items[0].IpAddress);
    }

    [Fact]
    public async Task Create_Clears_Form_On_Success()
    {
        var (vm, apiMock) = CreateVm();
        var createdDto = new IpBlockDto(Guid.NewGuid(), "1.2.3.4", true, null, "test", 0, null, DateTime.UtcNow, null);

        apiMock.Setup(a => a.Admin_CreateIpBlockAsync(It.IsAny<IpBlockCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdDto);
        apiMock.Setup(a => a.Admin_ListIpBlocksAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IpBlockDto>)new List<IpBlockDto>());

        vm.Ip = "1.2.3.4";
        vm.Reason = "test";

        await vm.CreateAsync();

        apiMock.Verify(a => a.Admin_CreateIpBlockAsync(It.Is<IpBlockCreateRequest>(r => r.IpAddress == "1.2.3.4"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(string.Empty, vm.Ip);
        Assert.Null(vm.Reason);
        Assert.True(vm.BlockOnCreate);
    }
}
