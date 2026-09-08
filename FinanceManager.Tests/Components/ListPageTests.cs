using Bunit;
using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Admin;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Web.Components.Pages;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using msTools.Web.Blazor;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Component tests for the route-backed generic list page.
/// </summary>
public sealed class ListPageTests : BunitContext
{
    /// <summary>
    /// Registers shared services required by page-level component rendering.
    /// </summary>
    public ListPageTests()
    {
        Services.AddScoped<LoadingBarService>();
        Services.AddSingleton<IConfirmationService>(NullConfirmationService.Instance);
        JSInterop.SetupVoid("financeManager.loadingBar.start").SetVoidResult();
        JSInterop.SetupVoid("financeManager.loadingBar.stop").SetVoidResult();
    }

    /// <summary>
    /// Verifies that embedded panels are scoped by list view models, rendered below the list, while the generic list remains available for other list kinds.
    /// </summary>
    [Fact]
    public void ListPage_Accounts_RendersStatisticsAndGenericListOnlyForAccounts()
    {
        RegisterServices();

        var accountsPage = Render<ListPage>(parameters => parameters.Add(p => p.Kind, "accounts"));

        accountsPage.WaitForAssertion(() =>
        {
            Assert.Single(accountsPage.FindAll(".accounts-statistics"));
            Assert.Single(accountsPage.FindAll(".generic-list-table-wrap"));
            Assert.True(
                accountsPage.Markup.IndexOf("generic-list-table-wrap", StringComparison.Ordinal)
                    < accountsPage.Markup.IndexOf("accounts-statistics", StringComparison.Ordinal),
                "Embedded account statistics should be rendered below the bank account table.");
        });

        var statementDraftsPage = Render<ListPage>(parameters => parameters.Add(p => p.Kind, "statement-drafts"));

        statementDraftsPage.WaitForAssertion(() =>
        {
            Assert.Empty(statementDraftsPage.FindAll(".accounts-statistics"));
            Assert.Single(statementDraftsPage.FindAll(".generic-list-table-wrap"));
        });
    }

    private void RegisterServices()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.GetAccountsAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AccountDto(
                    Guid.NewGuid(),
                    "Checking",
                    AccountType.Giro,
                    "DE11",
                    123m,
                    Guid.NewGuid(),
                    null,
                    SavingsPlanExpectation.Optional,
                    true)
            });
        apiMock.Setup(a => a.GetAccountStatisticsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountStatisticsDto(
                AccountCount: 1,
                TotalBalance: 123m,
                YearToDateChange: 0m,
                MonthToDateChange: 0m,
                TotalGrossMagnitude: 123m,
                ByAccountType:
                [
                    new AccountBalanceGroupDto(AccountType.Giro.ToString(), null, 123m, 123m, 0m, 123m, 1, 0)
                ],
                ByBankContact:
                [
                    new AccountBalanceGroupDto("bank", "Bank", 123m, 123m, 0m, 123m, 1, 0)
                ]));
        apiMock.Setup(a => a.StatementDrafts_ListOpenAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StatementDraftDto>());
        apiMock.Setup(a => a.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BackgroundTaskInfo>());

        Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        Services.AddSingleton(apiMock.Object);
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(PassthroughLocalizer<>));
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string? PreferredLanguage => "en";
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private sealed class PassthroughLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
