using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class AccountDetailViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId => Guid.NewGuid();
        public string? PreferredLanguage => "de";
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin => false;
    }

    private sealed class DummyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private static (AccountDetailViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        var currentUser = new TestCurrentUserService { IsAuthenticated = true };
        services.AddSingleton<ICurrentUserService>(currentUser);
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new AccountDetailViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_LoadsContacts_AndExistingAccount()
    {
        var accountId = Guid.NewGuid();
        var bankContactId = Guid.NewGuid();
        var (vm, apiMock) = CreateVm();

        apiMock.Setup(a => a.Contacts_ListAsync(0, 200, ContactType.Bank, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactDto>
            {
                new ContactDto(Guid.NewGuid(), "Bank A", ContactType.Bank, null, null, false, null),
                new ContactDto(Guid.NewGuid(), "Bank B", ContactType.Bank, null, null, false, null)
            });

        apiMock.Setup(a => a.GetAccountAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountDto(accountId, "My Account", AccountType.Giro, "DE00", 0, bankContactId, null, SavingsPlanExpectation.Optional));

        vm.ForAccount(accountId);
        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.NotEmpty(vm.BankContacts);
        Assert.Equal("My Account", vm.Name);
        Assert.Equal(AccountType.Giro, vm.Type);
        Assert.Equal("DE00", vm.Iban);
        Assert.False(vm.IsNew);
        Assert.True(vm.ShowCharts);
    }

    [Fact]
    public async Task Initialize_NewAccount_LoadsOnlyContacts()
    {
        var (vm, apiMock) = CreateVm();

        apiMock.Setup(a => a.Contacts_ListAsync(0, 200, ContactType.Bank, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactDto> { new ContactDto(Guid.NewGuid(), "Bank A", ContactType.Bank, null, null, false, null) });

        vm.ForAccount(null);
        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Single(vm.BankContacts);
        Assert.True(vm.IsNew);
        Assert.False(vm.ShowCharts);
    }

    [Fact]
    public async Task SaveAsync_New_PostsAndReturnsId()
    {
        var createdId = Guid.NewGuid();
        var (vm, apiMock) = CreateVm();

        apiMock.Setup(a => a.Contacts_ListAsync(0, 200, ContactType.Bank, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactDto>());

        apiMock.Setup(a => a.CreateAccountAsync(It.IsAny<AccountCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountDto(createdId, "Created", AccountType.Savings, null, 0, Guid.NewGuid(), null, SavingsPlanExpectation.Optional));

        vm.ForAccount(null);
        vm.Name = "New";
        vm.Type = AccountType.Savings;

        await vm.InitializeAsync();
        var id = await vm.SaveAsync();

        Assert.True(id.HasValue);
        Assert.Equal(createdId, id.Value);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public async Task SaveAsync_Update_PutsAndKeepsId()
    {
        var accountId = Guid.NewGuid();
        var (vm, apiMock) = CreateVm();

        apiMock.Setup(a => a.Contacts_ListAsync(0, 200, ContactType.Bank, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactDto>());

        apiMock.Setup(a => a.UpdateAccountAsync(accountId, It.IsAny<AccountUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountDto(accountId, "Existing", AccountType.Giro, null, 0, Guid.NewGuid(), null, SavingsPlanExpectation.Optional));

        vm.ForAccount(accountId);
        vm.Name = "Existing";
        vm.Type = AccountType.Giro;

        await vm.InitializeAsync();
        var id = await vm.SaveAsync();

        Assert.Null(id);
        Assert.False(vm.IsNew);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task DeleteAsync_Success_ClearsError()
    {
        var accountId = Guid.NewGuid();
        var (vm, apiMock) = CreateVm();

        apiMock.Setup(a => a.DeleteAccountAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        vm.ForAccount(accountId);
        await vm.DeleteAsync();

        Assert.Null(vm.Error);
    }

    [Fact]
    public void GetRibbon_ContainsExpectedGroups()
    {
        var (vm, _) = CreateVm();
        var loc = new DummyLocalizer();

        vm.ForAccount(null);
        var groupsNew = vm.GetRibbon(loc);
        Assert.Contains(groupsNew, g => g.Title == "Ribbon_Group_Navigation");
        Assert.Contains(groupsNew, g => g.Title == "Ribbon_Group_Edit");
        Assert.DoesNotContain(groupsNew, g => g.Title == "Ribbon_Group_Related");

        vm.ForAccount(Guid.NewGuid());
        vm.Name = "Acc";
        var groupsExisting = vm.GetRibbon(loc);
        Assert.Contains(groupsExisting, g => g.Title == "Ribbon_Group_Related");
    }
}
