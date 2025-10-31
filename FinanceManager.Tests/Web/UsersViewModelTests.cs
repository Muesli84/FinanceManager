using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FinanceManager.Tests.Web;

public sealed class UsersViewModelTests
{
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod method, Regex path, Func<HttpRequestMessage, HttpResponseMessage> handler)> _routes = new();
        public void Map(HttpMethod method, string pathPattern, Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _routes.Add((method, new Regex(pathPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase), handler));
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var match = _routes.FirstOrDefault(r => r.method == request.Method && r.path.IsMatch(request.RequestUri!.AbsolutePath));
            if (match.handler != null)
            {
                return Task.FromResult(match.handler(request));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) { _client = client; }
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = true;
    }

    private static (UsersViewModel vm, RouteHandler handler) CreateVmWithRoutes()
    {
        var handler = new RouteHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var factory = new TestHttpClientFactory(client);
        // Minimal service provider with current user
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUserService>(new TestCurrentUserService())
            .BuildServiceProvider();
        var vm = new UsersViewModel(services, factory);
        return (vm, handler);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadUsers_AndSetLoaded()
    {
        var (vm, router) = CreateVmWithRoutes();
        var users = new[] { new UsersViewModel.UserVm { Id = Guid.NewGuid(), Username = "u1", Active = true } };
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(users) });

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Single(vm.Users);
        Assert.Equal("u1", vm.Users[0].Username);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task CreateAsync_ShouldPostAppendAndReset()
    {
        var (vm, router) = CreateVmWithRoutes();
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<UsersViewModel.UserVm>()) });
        var created = new UsersViewModel.UserVm { Id = Guid.NewGuid(), Username = "new", Active = true };
        router.Map(HttpMethod.Post, "/api/admin/users$", req => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(created) });

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
        var (vm, router) = CreateVmWithRoutes();
        var user = new UsersViewModel.UserVm { Id = Guid.NewGuid(), Username = "old", Active = true };
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { user }) });
        var updated = new UsersViewModel.UserVm { Id = user.Id, Username = "updated", Active = false };
        router.Map(HttpMethod.Put, $"/api/admin/users/{user.Id}$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(updated) });

        await vm.InitializeAsync();
        vm.BeginEdit(vm.Users[0]);
        vm.EditUsername = "updated"; vm.EditActive = false;

        await vm.SaveEditAsync(user.Id);

        Assert.Equal("updated", vm.Users.Single().Username);
        Assert.Null(vm.Edit);
        Assert.False(vm.BusyRow);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveUser()
    {
        var (vm, router) = CreateVmWithRoutes();
        var id = Guid.NewGuid();
        var list = new[] { new UsersViewModel.UserVm { Id = id, Username = "to-del", Active = true } };
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(list) });
        router.Map(HttpMethod.Delete, $"/api/admin/users/{id}$", _ => new HttpResponseMessage(HttpStatusCode.OK));

        await vm.InitializeAsync();
        Assert.Single(vm.Users);

        await vm.DeleteAsync(id);

        Assert.Empty(vm.Users);
        Assert.False(vm.BusyRow);
    }

    [Fact]
    public async Task ResetPasswordAsync_ShouldSetLastResetFields()
    {
        var (vm, router) = CreateVmWithRoutes();
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<UsersViewModel.UserVm>()) });
        var id = Guid.NewGuid();
        router.Map(HttpMethod.Post, $"/api/admin/users/{id}/reset-password$", _ => new HttpResponseMessage(HttpStatusCode.OK));

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
        var (vm, router) = CreateVmWithRoutes();
        var id = Guid.NewGuid();
        var user = new UsersViewModel.UserVm { Id = id, Username = "u", Active = true, LockoutEnd = DateTime.UtcNow.AddHours(1) };
        router.Map(HttpMethod.Get, "/api/admin/users$", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { user }) });
        router.Map(HttpMethod.Post, $"/api/admin/users/{id}/unlock$", _ => new HttpResponseMessage(HttpStatusCode.OK));

        await vm.InitializeAsync();
        Assert.NotNull(vm.Users.Single().LockoutEnd);

        await vm.UnlockAsync(id);

        Assert.Null(vm.Users.Single().LockoutEnd);
    }

    [Fact]
    public void GetRibbon_ShouldComposeGroups_ByState()
    {
        var (vm, router) = CreateVmWithRoutes();
        // no HTTP required
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
        // simulate last reset
        var field = typeof(UsersViewModel).GetProperty("LastResetUserId");
        vm.GetType().GetProperty("LastResetUserId")!.SetValue(vm, Guid.NewGuid());
        vm.GetType().GetProperty("LastResetPassword")!.SetValue(vm, "abcdef123456");
        groups = vm.GetRibbon(loc);
        Assert.Contains(groups[1].Items, i => i.Label == "Ribbon_HidePassword");
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get => new(name, name);
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get => new(name, string.Format(name, arguments));
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            yield break;
        }

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture)
        {
            return this;
        }
    }
}
