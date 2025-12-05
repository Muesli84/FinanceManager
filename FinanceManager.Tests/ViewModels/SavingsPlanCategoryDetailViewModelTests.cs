using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SavingsPlanCategoryDetailViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (SavingsPlanCategoryDetailViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SavingsPlanCategoryDetailViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Edit_Loads_Model()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var dto = new SavingsPlanCategoryDto { Id = id, Name = "Cat1" };
        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        await vm.InitializeAsync(id);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Cat1", vm.Model.Name);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Initialize_Edit_NotFound_Sets_Error()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsPlanCategoryDto?)null);

        await vm.InitializeAsync(id);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Error_NotFound", vm.Error);
    }

    [Fact]
    public async Task Save_New_Success()
    {
        var (vm, apiMock) = CreateVm();
        var createdDto = new SavingsPlanCategoryDto { Id = Guid.NewGuid(), Name = "NewCat" };
        apiMock.Setup(a => a.SavingsPlanCategories_CreateAsync(It.IsAny<SavingsPlanCategoryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdDto);

        await vm.InitializeAsync(null);
        vm.Model.Name = "NewCat";
        var ok = await vm.SaveAsync();

        Assert.True(ok);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Save_New_Fail()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.SavingsPlanCategories_CreateAsync(It.IsAny<SavingsPlanCategoryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsPlanCategoryDto?)null);
        apiMock.SetupGet(a => a.LastError).Returns("bad");

        await vm.InitializeAsync(null);
        vm.Model.Name = "X";
        var ok = await vm.SaveAsync();

        Assert.False(ok);
        Assert.Contains("bad", vm.Error);
    }

    [Fact]
    public async Task Save_Edit_Success()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var existingDto = new SavingsPlanCategoryDto { Id = id, Name = "Cat" };
        var updatedDto = new SavingsPlanCategoryDto { Id = id, Name = "Updated" };

        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDto);
        apiMock.Setup(a => a.SavingsPlanCategories_UpdateAsync(id, It.IsAny<SavingsPlanCategoryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        await vm.InitializeAsync(id);
        vm.Model.Name = "Updated";
        var ok = await vm.SaveAsync();

        Assert.True(ok);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Delete_Success()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var existingDto = new SavingsPlanCategoryDto { Id = id, Name = "Cat" };

        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDto);
        apiMock.Setup(a => a.SavingsPlanCategories_DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync(id);
        var ok = await vm.DeleteAsync();

        Assert.True(ok);
    }

    [Fact]
    public async Task Delete_Fail()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var existingDto = new SavingsPlanCategoryDto { Id = id, Name = "Cat" };

        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDto);
        apiMock.Setup(a => a.SavingsPlanCategories_DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        apiMock.SetupGet(a => a.LastError).Returns("bad");

        await vm.InitializeAsync(id);
        var ok = await vm.DeleteAsync();

        Assert.False(ok);
        Assert.Contains("bad", vm.Error);
    }

    [Fact]
    public async Task Ribbon_Disables_Save_When_Name_Short()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var dto = new SavingsPlanCategoryDto { Id = id, Name = "C" };
        apiMock.Setup(a => a.SavingsPlanCategories_GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        await vm.InitializeAsync(id);
        var loc = new TestLocalizer<SavingsPlanCategoryDetailViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var edit = groups.First(g => g.Title == "Ribbon_Group_Edit");
        var save = edit.Items.First(i => i.Action == "Save");
        var del = edit.Items.First(i => i.Action == "Delete");
        Assert.True(save.Disabled);
        Assert.False(del.Disabled);

        vm.Model.Name = "OK";
        groups = vm.GetRibbon(loc);
        save = groups.First(g => g.Title == "Ribbon_Group_Edit").Items.First(i => i.Action == "Save");
        Assert.False(save.Disabled);
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
    }
}
