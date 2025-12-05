using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Attachments;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupAttachmentCategoriesViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (SetupAttachmentCategoriesViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SetupAttachmentCategoriesViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Loads_And_Sorts()
    {
        var (vm, apiMock) = CreateVm();
        var categories = new List<AttachmentCategoryDto>
        {
            new AttachmentCategoryDto(Guid.NewGuid(), "B", false, false),
            new AttachmentCategoryDto(Guid.NewGuid(), "A", false, false)
        };
        apiMock.Setup(a => a.Attachments_ListCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        await vm.InitializeAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(new[] { "A", "B" }, vm.Items.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task AddAsync_Adds_And_Clears_And_Sets_ActionOk()
    {
        var (vm, apiMock) = CreateVm();
        var createdId = Guid.NewGuid();
        var created = new AttachmentCategoryDto(createdId, "Zeta", false, false);

        apiMock.Setup(a => a.Attachments_ListCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttachmentCategoryDto>());
        apiMock.Setup(a => a.Attachments_CreateCategoryAsync("Zeta", It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        await vm.InitializeAsync();
        vm.NewName = "Zeta";
        await vm.AddAsync();

        Assert.True(vm.ActionOk);
        Assert.Equal(string.Empty, vm.NewName);
        Assert.Single(vm.Items);
        Assert.Equal("Zeta", vm.Items[0].Name);
    }

    [Fact]
    public async Task BeginEdit_And_SaveEdit_Updates_Item()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var initial = new AttachmentCategoryDto(id, "Old", false, false);
        var updated = new AttachmentCategoryDto(id, "New", false, false);

        apiMock.Setup(a => a.Attachments_ListCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttachmentCategoryDto> { initial });
        apiMock.Setup(a => a.Attachments_UpdateCategoryNameAsync(id, "New", It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        await vm.InitializeAsync();

        vm.BeginEdit(id, "Old");
        Assert.Equal(id, vm.EditId);
        vm.EditName = "New";
        await vm.SaveEditAsync();

        Assert.True(vm.ActionOk);
        Assert.Equal(Guid.Empty, vm.EditId);
        Assert.Single(vm.Items);
        Assert.Equal("New", vm.Items[0].Name);
    }

    [Fact]
    public async Task Delete_Removes_Item()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var initial = new AttachmentCategoryDto(id, "ToDelete", false, false);

        apiMock.Setup(a => a.Attachments_ListCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttachmentCategoryDto> { initial });
        apiMock.Setup(a => a.Attachments_DeleteCategoryAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync();

        Assert.Single(vm.Items);
        await vm.DeleteAsync(id);
        Assert.True(vm.ActionOk);
        Assert.Empty(vm.Items);
    }
}
