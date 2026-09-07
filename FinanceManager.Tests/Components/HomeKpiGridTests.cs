using Bunit;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Shared.Dtos.HomeKpi;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Shared.Dtos.Reports;
using FinanceManager.Web;
using FinanceManager.Web.Components.Shared;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Verifies the behavior of the <see cref="HomeKpiGrid"/> dashboard component: that each configured home KPI
/// (predefined or report-favorite backed) issues the correct API request, and that KPIs load and render
/// independently of one another so a slow or pending KPI cannot block the rest of the grid.
/// </summary>
public sealed class HomeKpiGridTests : BunitContext
{
    /// <summary>
    /// Registers the DI services (logging, localization, string localizer, system time provider) that
    /// <see cref="HomeKpiGrid"/> and its child KPI components need in order to render inside the bUnit test context.
    /// </summary>
    public HomeKpiGridTests()
    {
        Services.AddLogging();
        Services.AddLocalization(options => options.ResourcesPath = "Resources");
        Services.AddSingleton(typeof(IStringLocalizer<Pages>), new PagesStringLocalizer());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IConfirmationService>(Mock.Of<IConfirmationService>());
    }

    /// <summary>
    /// Verifies that a home KPI backed by a report favorite (<see cref="HomeKpiKind.ReportFavorite"/>) forwards
    /// the favorite's comparison flags (compare-to-previous-year, compare-to-projection) and its valuta-date
    /// setting into the <see cref="ReportAggregatesQueryRequest"/> sent to the API, so the grid's chart reflects
    /// exactly the comparison options configured on the underlying favorite rather than some default.
    /// </summary>
    [Fact]
    public void ReportFavoriteKpi_ForwardsCompareProjectionToAggregateRequest()
    {
        var kpiId = Guid.NewGuid();
        var favoriteId = Guid.NewGuid();
        var apiMock = new Mock<IApiClient>();
        ReportAggregatesQueryRequest? capturedRequest = null;

        apiMock.Setup(a => a.HomeKpis_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new HomeKpiDto(
                    kpiId,
                    HomeKpiKind.ReportFavorite,
                    favoriteId,
                    "Projected dividends",
                    null,
                    null,
                    HomeKpiDisplayMode.TotalOnly,
                    0,
                    DateTime.UtcNow,
                    null)
            });

        apiMock.Setup(a => a.Reports_GetFavoriteAsync(favoriteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportFavoriteDto(
                favoriteId,
                "Projected dividends",
                PostingKind.Security,
                IncludeCategory: false,
                ReportInterval.Month,
                Take: 6,
                ComparePrevious: true,
                CompareYear: true,
                CompareProjection: true,
                ShowChart: true,
                Expandable: true,
                DateTime.UtcNow,
                ModifiedUtc: null,
                new[] { PostingKind.Security },
                Filters: null,
                UseValutaDate: true));

        apiMock.Setup(a => a.Reports_QueryAggregatesAsync(It.IsAny<ReportAggregatesQueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ReportAggregatesQueryRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ReportAggregationResult(
                ReportInterval.Month,
                new[]
                {
                    new ReportAggregatePointDto(
                        new DateTime(2026, 7, 1),
                        "Security:1",
                        "Security",
                        null,
                        10m,
                        12m,
                        null,
                        8m,
                        7m)
                },
                ComparedPrevious: true,
                ComparedYear: true,
                ComparedProjection: true));

        Services.AddSingleton(apiMock.Object);

        var cut = Render<HomeKpiGrid>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest!.CompareProjection);
            Assert.True(capturedRequest.CompareYear);
            Assert.True(capturedRequest.UseValutaDate);
        });
    }

    /// <summary>
    /// Verifies that a monthly-budget KPI whose API call never completes (a pending <see cref="TaskCompletionSource{TResult}"/>)
    /// does not prevent a second, independent KPI (contacts count) from loading and rendering its own markup.
    /// Guards against a regression where the grid would await KPIs sequentially instead of loading each tile
    /// independently, which would make one slow KPI stall the entire dashboard.
    /// </summary>
    [Fact]
    public void MonthlyBudgetKpi_DoesNotBlockOtherHomeKpiRendering()
    {
        var monthlyKpiId = Guid.NewGuid();
        var contactsKpiId = Guid.NewGuid();
        var pendingMonthlyKpi = new TaskCompletionSource<MonthlyBudgetKpiDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiMock = new Mock<IApiClient>();

        apiMock.Setup(a => a.HomeKpis_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreatePredefinedKpi(monthlyKpiId, HomeKpiPredefined.MonthlyBudget, 0),
                CreatePredefinedKpi(contactsKpiId, HomeKpiPredefined.ContactsCount, 1)
            });
        apiMock.Setup(a => a.Budgets_GetMonthlyKpiAsync(
                null,
                BudgetReportDateBasis.BookingDate,
                It.IsAny<CancellationToken>()))
            .Returns(pendingMonthlyKpi.Task);
        apiMock.Setup(a => a.Contacts_CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<HomeKpiGrid>();

        cut.WaitForAssertion(() =>
        {
            apiMock.Verify(a => a.Budgets_GetMonthlyKpiAsync(
                null,
                BudgetReportDateBasis.BookingDate,
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotEmpty(cut.FindAll("a[href='/list/contacts']"));
        });
    }

    /// <summary>
    /// Verifies that forcing a re-render of the grid (<c>cut.Render()</c>) while the monthly-budget KPI request
    /// is still pending does not trigger a duplicate call to <c>Budgets_GetMonthlyKpiAsync</c>. Protects against
    /// the KPI re-issuing its data request on every parent re-render instead of only once per load.
    /// </summary>
    [Fact]
    public void MonthlyBudgetKpi_ReRenderDoesNotCreateSecondRequest()
    {
        var monthlyKpiId = Guid.NewGuid();
        var pendingMonthlyKpi = new TaskCompletionSource<MonthlyBudgetKpiDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiMock = new Mock<IApiClient>();

        apiMock.Setup(a => a.HomeKpis_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreatePredefinedKpi(monthlyKpiId, HomeKpiPredefined.MonthlyBudget, 0)
            });
        apiMock.Setup(a => a.Budgets_GetMonthlyKpiAsync(
                null,
                BudgetReportDateBasis.BookingDate,
                It.IsAny<CancellationToken>()))
            .Returns(pendingMonthlyKpi.Task);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<HomeKpiGrid>();
        cut.WaitForAssertion(() => apiMock.Verify(a => a.Budgets_GetMonthlyKpiAsync(
            null,
            BudgetReportDateBasis.BookingDate,
            It.IsAny<CancellationToken>()), Times.Once));

        cut.Render();

        apiMock.Verify(a => a.Budgets_GetMonthlyKpiAsync(
            null,
            BudgetReportDateBasis.BookingDate,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Builds a <see cref="HomeKpiDto"/> for one of the built-in (non report-favorite) KPI types, with the given
    /// identity and grid sort position, so tests can stub <c>HomeKpis_ListAsync</c> without repeating the DTO's
    /// unused fields at every call site.
    /// </summary>
    /// <param name="id">Identifier assigned to the generated KPI entry.</param>
    /// <param name="predefinedType">Which built-in KPI (e.g. monthly budget, contacts count) to represent.</param>
    /// <param name="sortOrder">Position of the KPI within the grid.</param>
    /// <returns>A <see cref="HomeKpiDto"/> configured as a predefined KPI of the given type.</returns>
    private static HomeKpiDto CreatePredefinedKpi(Guid id, HomeKpiPredefined predefinedType, int sortOrder) =>
        new(
            id,
            HomeKpiKind.Predefined,
            null,
            null,
            null,
            predefinedType,
            HomeKpiDisplayMode.TotalOnly,
            sortOrder,
            DateTime.UtcNow,
            null);
}
