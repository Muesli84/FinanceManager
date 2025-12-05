using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Web;

public sealed class UsersViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = true;
    }

    private static (UsersViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var apiMock = new Mock<IApiClient>();
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUserService>(new TestCurrentUserService())
            .AddSingleton(apiMock.Object)
            .BuildServiceProvider();
        var vm = new UsersViewModel(services);
        return (vm, apiMock);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadUsers_AndSetLoaded()
    {
        var (vm, apiMock) = CreateVm();
        var users = new List<UserAdminDto>
        {
            new UserAdminDto(Guid.NewGuid(), "u1", false, true, null, DateTime.UtcNow, null)
        };
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Single(vm.Users);
        Assert.Equal("u1", vm.Users[0].Username);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task CreateAsync_ShouldPostAppendAndReset()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAdminDto>());

        var createdId = Guid.NewGuid();
        apiMock.Setup(a => a.Admin_CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminDto(createdId, "new", true, true, null, DateTime.UtcNow, null));

        await vm.InitializeAsync();
        vm.Create.Username = "new"; vm.Create.Password = "secret123"; vm.Create.IsAdmin = true;

        await vm.CreateAsync();

        Assert.Single(vm.Users, u => u.Username == "new");
        Assert.Equal(string.Empty, vm.Create.Username);
        Assert.False(vm.BusyCreate);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task BeginEdit_SaveEditAsync_ShouldUpdateUser_AndClearEdit()
    {
        var (vm, apiMock) = CreateVm();
        var userId = Guid.NewGuid();
        var users = new List<UserAdminDto>
        {
            new UserAdminDto(userId, "old", false, true, null, DateTime.UtcNow, null)
        };
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        apiMock.Setup(a => a.Admin_UpdateUserAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminDto(userId, "updated", false, false, null, DateTime.UtcNow, null));

        await vm.InitializeAsync();
        vm.BeginEdit(vm.Users[0]);
        vm.EditUsername = "updated"; vm.EditActive = false;

        await vm.SaveEditAsync(userId);

        Assert.Equal("updated", vm.Users.Single().Username);
        Assert.Null(vm.Edit);
        Assert.False(vm.BusyRow);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveUser()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var users = new List<UserAdminDto>
        {
            new UserAdminDto(id, "to-del", false, true, null, DateTime.UtcNow, null)
        };
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);
        apiMock.Setup(a => a.Admin_DeleteUserAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync();
        Assert.Single(vm.Users);

        await vm.DeleteAsync(id);

        Assert.Empty(vm.Users);
        Assert.False(vm.BusyRow);
    }

    [Fact]
    public async Task ResetPasswordAsync_ShouldSetLastResetFields()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAdminDto>());

        var id = Guid.NewGuid();
        apiMock.Setup(a => a.Admin_ResetPasswordAsync(id, It.IsAny<ResetPasswordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync();
        await vm.ResetPasswordAsync(id);

        Assert.Equal(id, vm.LastResetUserId);
        Assert.False(string.IsNullOrEmpty(vm.LastResetPassword));
        Assert.Equal(12, vm.LastResetPassword!.Length);

        vm.ClearLastPassword();
        Assert.Equal(Guid.Empty, vm.LastResetUserId);
        Assert.Null(vm.LastResetPassword);
    }

    [Fact]
    public async Task UnlockAsync_ShouldClearLockoutEnd()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var users = new List<UserAdminDto>
        {
            new UserAdminDto(id, "u", false, true, DateTime.UtcNow.AddHours(1), DateTime.UtcNow, null)
        };
        apiMock.Setup(a => a.Admin_ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);
        apiMock.Setup(a => a.Admin_UnlockUserAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync();
        Assert.NotNull(vm.Users.Single().LockoutEnd);

        await vm.UnlockAsync(id);

        Assert.Null(vm.Users.Single().LockoutEnd);
    }

    [Fact]
    public void GetRibbon_ShouldComposeGroups_ByState()
    {
        var (vm, _) = CreateVm();
        var loc = new FakeLocalizer();

        // base state
        var groups = vm.GetRibbon(loc);
        Assert.Equal(2, groups.Count);
        Assert.Equal("Ribbon_Group_Navigation", groups[0].Title);
        Assert.Equal("Ribbon_Group_Actions", groups[1].Title);
        Assert.Contains(groups[1].Items, i => i.Label == "Ribbon_Reload");

        // with edit
        vm.BeginEdit(new UsersViewModel.UserVm { Id = Guid.NewGuid(), Username = "x" });
        groups = vm.GetRibbon(loc);
        Assert.Contains(groups[1].Items, i => i.Label == "Ribbon_CancelEdit");

        // with last reset password
        vm.CancelEdit();
        vm.GetType().GetProperty("LastResetUserId")!.SetValue(vm, Guid.NewGuid());
        vm.GetType().GetProperty("LastResetPassword")!.SetValue(vm, "abcdef123456");
        groups = vm.GetRibbon(loc);
        Assert.Contains(groups[1].Items, i => i.Label == "Ribbon_HidePassword");
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
    }
}
