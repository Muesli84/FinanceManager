namespace FinanceManager.Tests.Common;

/// <summary>
/// Guards the public/internal enums of the application against unintended changes:
/// each test asserts the exact set of enum member names, so adding, renaming or
/// removing a value fails loudly instead of going unnoticed.
/// </summary>
public sealed class EnumCoverageTests
{
    private static void AssertValues<TEnum>(params string[] expected) where TEnum : struct, Enum
    {
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), Enum.GetNames<TEnum>().OrderBy(x => x, StringComparer.Ordinal));
        foreach (var name in expected)
        {
            Assert.True(Enum.TryParse<TEnum>(name, out _), $"Enum value {typeof(TEnum).Name}.{name} must be parseable.");
        }
    }

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void AccountShareRole_Values()
        => AssertValues<FinanceManager.Domain.AccountShareRole>(
            nameof(FinanceManager.Domain.AccountShareRole.Read),
            nameof(FinanceManager.Domain.AccountShareRole.Write),
            nameof(FinanceManager.Domain.AccountShareRole.Admin));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void StatementEntryStatus_Values()
        => AssertValues<FinanceManager.Domain.StatementEntryStatus>(
            nameof(FinanceManager.Domain.StatementEntryStatus.Pending),
            nameof(FinanceManager.Domain.StatementEntryStatus.Booked),
            nameof(FinanceManager.Domain.StatementEntryStatus.IgnoredDuplicate));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BudgetReportValueScope_Shared_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Budget.BudgetReportValueScope>(
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportValueScope.TotalRange),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportValueScope.LastInterval));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BudgetReportValueScope_Web_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Budget.BudgetReportValueScope>(
            nameof(FinanceManager.Web.ViewModels.Budget.BudgetReportValueScope.TotalRange),
            nameof(FinanceManager.Web.ViewModels.Budget.BudgetReportValueScope.LastInterval));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void PostingsOverlayKind_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Budget.BudgetReportViewModel.PostingsOverlayKind>(
            nameof(FinanceManager.Web.ViewModels.Budget.BudgetReportViewModel.PostingsOverlayKind.Purpose),
            nameof(FinanceManager.Web.ViewModels.Budget.BudgetReportViewModel.PostingsOverlayKind.Unbudgeted));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BooleanSelection_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.BooleanSelection>(
            nameof(FinanceManager.Web.ViewModels.Common.BooleanSelection.True),
            nameof(FinanceManager.Web.ViewModels.Common.BooleanSelection.False));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void EmbeddedPanelPosition_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.EmbeddedPanelPosition>(
            nameof(FinanceManager.Web.ViewModels.Common.EmbeddedPanelPosition.AfterRibbon),
            nameof(FinanceManager.Web.ViewModels.Common.EmbeddedPanelPosition.AfterCard),
            nameof(FinanceManager.Web.ViewModels.Common.EmbeddedPanelPosition.AfterList));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void ListCellKind_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.ListCellKind>(
            nameof(FinanceManager.Web.ViewModels.Common.ListCellKind.Text),
            nameof(FinanceManager.Web.ViewModels.Common.ListCellKind.Symbol),
            nameof(FinanceManager.Web.ViewModels.Common.ListCellKind.Currency));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void ListColumnAlign_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.ListColumnAlign>(
            nameof(FinanceManager.Web.ViewModels.Common.ListColumnAlign.Left),
            nameof(FinanceManager.Web.ViewModels.Common.ListColumnAlign.Center),
            nameof(FinanceManager.Web.ViewModels.Common.ListColumnAlign.Right));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void CardFieldKind_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.CardFieldKind>(
            nameof(FinanceManager.Web.ViewModels.Common.CardFieldKind.Text),
            nameof(FinanceManager.Web.ViewModels.Common.CardFieldKind.Date),
            nameof(FinanceManager.Web.ViewModels.Common.CardFieldKind.Boolean),
            nameof(FinanceManager.Web.ViewModels.Common.CardFieldKind.Symbol),
            nameof(FinanceManager.Web.ViewModels.Common.CardFieldKind.Currency));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void UiRibbonItemSize_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.UiRibbonItemSize>(
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonItemSize.Small),
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonItemSize.Large));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void UiRibbonRegisterKind_Values()
        => AssertValues<FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind>(
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind.QuickAccess),
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind.Actions),
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind.LinkedInfo),
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind.Reports),
            nameof(FinanceManager.Web.ViewModels.Common.UiRibbonRegisterKind.Custom));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BackgroundTaskStatus_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus>(
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus.Queued),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus.Running),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus.Completed),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus.Failed),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskStatus.Cancelled));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BackgroundTaskType_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Admin.BackgroundTaskType>(
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.ClassifyAllDrafts),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.BookAllDrafts),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.BackupRestore),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.SecurityPricesBackfill),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.RebuildAggregates),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.RefreshBudgetReportCache),
            nameof(FinanceManager.Shared.Dtos.Admin.BackgroundTaskType.CreateDemoData));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BudgetReportCategoryRowKind_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind>(
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.Data),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.Sum),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.Unbudgeted),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.UnbudgetedSelfCostNeutral),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.UnbudgetedSubSum),
            nameof(FinanceManager.Shared.Dtos.Budget.BudgetReportCategoryRowKind.Result));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void HomeKpiPredefined_Values()
        => AssertValues<FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined>(
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.AccountsAggregates),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.SavingsPlanAggregates),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.SecuritiesDividends),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.MonthlyBudget),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.ActiveSavingsPlansCount),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.ContactsCount),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.SecuritiesCount),
            nameof(FinanceManager.Shared.Dtos.HomeKpi.HomeKpiPredefined.OpenStatementDraftsCount));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void SavingsPlanInterval_Values()
        => AssertValues<FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval>(
            nameof(FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval.Monthly),
            nameof(FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval.BiMonthly),
            nameof(FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval.Quarterly),
            nameof(FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval.SemiAnnually),
            nameof(FinanceManager.Shared.Dtos.SavingsPlans.SavingsPlanInterval.Annually));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void ChartTimeRange_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Securities.ChartTimeRange>(
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.OneMonth),
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.ThreeMonths),
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.SixMonths),
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.OneYear),
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.ThreeYears),
            nameof(FinanceManager.Shared.Dtos.Securities.ChartTimeRange.All));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void ImportFormat_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Statements.ImportFormat>(
            nameof(FinanceManager.Shared.Dtos.Statements.ImportFormat.Csv),
            nameof(FinanceManager.Shared.Dtos.Statements.ImportFormat.Pdf),
            nameof(FinanceManager.Shared.Dtos.Statements.ImportFormat.Backup),
            nameof(FinanceManager.Shared.Dtos.Statements.ImportFormat.Reversal));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void MassImportFileType_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Statements.MassImportFileType>(
            nameof(FinanceManager.Shared.Dtos.Statements.MassImportFileType.Unknown),
            nameof(FinanceManager.Shared.Dtos.Statements.MassImportFileType.AccountStatement),
            nameof(FinanceManager.Shared.Dtos.Statements.MassImportFileType.SecurityPrices));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void MassImportDecisionSource_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Statements.MassImportDecisionSource>(
            nameof(FinanceManager.Shared.Dtos.Statements.MassImportDecisionSource.AutoDetected),
            nameof(FinanceManager.Shared.Dtos.Statements.MassImportDecisionSource.UserConfirmed));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void StatementDraftStatus_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Statements.StatementDraftStatus>(
            nameof(FinanceManager.Shared.Dtos.Statements.StatementDraftStatus.Draft),
            nameof(FinanceManager.Shared.Dtos.Statements.StatementDraftStatus.Committed),
            nameof(FinanceManager.Shared.Dtos.Statements.StatementDraftStatus.Expired));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void UpdateStatusKind_Values()
        => AssertValues<FinanceManager.Shared.Dtos.Update.UpdateStatusKind>(
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.NoUpdate),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Checking),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Available),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Downloading),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Ready),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Installing),
            nameof(FinanceManager.Shared.Dtos.Update.UpdateStatusKind.Failed));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void LineParsingMode_Values()
        => AssertValues<FinanceManager.Infrastructure.Statements.Files.PdfStatementFile.LineParsingMode>(
            nameof(FinanceManager.Infrastructure.Statements.Files.PdfStatementFile.LineParsingMode.TextOnly),
            nameof(FinanceManager.Infrastructure.Statements.Files.PdfStatementFile.LineParsingMode.TablesOnly),
            nameof(FinanceManager.Infrastructure.Statements.Files.PdfStatementFile.LineParsingMode.TextAndTables));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void AttachmentRole_Values()
        => AssertValues<FinanceManager.Domain.Attachments.AttachmentRole>(
            nameof(FinanceManager.Domain.Attachments.AttachmentRole.Regular),
            nameof(FinanceManager.Domain.Attachments.AttachmentRole.Symbol));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void HolidayProviderKind_Values()
        => AssertValues<FinanceManager.Domain.Notifications.HolidayProviderKind>(
            nameof(FinanceManager.Domain.Notifications.HolidayProviderKind.Memory),
            nameof(FinanceManager.Domain.Notifications.HolidayProviderKind.NagerDate));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void NotificationTarget_Values()
        => AssertValues<FinanceManager.Domain.Notifications.NotificationTarget>(
            nameof(FinanceManager.Domain.Notifications.NotificationTarget.HomePage),
            nameof(FinanceManager.Domain.Notifications.NotificationTarget.Dashboard),
            nameof(FinanceManager.Domain.Notifications.NotificationTarget.Modal),
            nameof(FinanceManager.Domain.Notifications.NotificationTarget.Toast));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void NotificationType_Values()
        => AssertValues<FinanceManager.Domain.Notifications.NotificationType>(
            nameof(FinanceManager.Domain.Notifications.NotificationType.MonthlyReminder),
            nameof(FinanceManager.Domain.Notifications.NotificationType.EventDriven),
            nameof(FinanceManager.Domain.Notifications.NotificationType.SystemAlert));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void ReportEntityGroup_Values()
        => AssertValues<FinanceManager.Domain.Reports.ReportEntityGroup>(
            nameof(FinanceManager.Domain.Reports.ReportEntityGroup.Account),
            nameof(FinanceManager.Domain.Reports.ReportEntityGroup.Contact),
            nameof(FinanceManager.Domain.Reports.ReportEntityGroup.SavingsPlan),
            nameof(FinanceManager.Domain.Reports.ReportEntityGroup.Security));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void BackupApplyStatus_Values()
        => AssertValues<FinanceManager.Application.Backups.BackupApplyStatus>(
            nameof(FinanceManager.Application.Backups.BackupApplyStatus.Succeeded),
            nameof(FinanceManager.Application.Backups.BackupApplyStatus.NotFound),
            nameof(FinanceManager.Application.Backups.BackupApplyStatus.InvalidBackup),
            nameof(FinanceManager.Application.Backups.BackupApplyStatus.ConfirmationRequired),
            nameof(FinanceManager.Application.Backups.BackupApplyStatus.ImportFailed));

    /// <summary>
    /// Verifies the declared values of the enum covered by this test.
    /// </summary>
    [Fact]
    public void PostingExportFormat_Values()
        => AssertValues<FinanceManager.Application.Reports.PostingExportFormat>(
            nameof(FinanceManager.Application.Reports.PostingExportFormat.Csv),
            nameof(FinanceManager.Application.Reports.PostingExportFormat.Xlsx));
}
