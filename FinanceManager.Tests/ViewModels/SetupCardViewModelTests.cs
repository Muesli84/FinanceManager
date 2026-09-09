using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using Xunit;
using FinanceManager.Web.ViewModels.Setup;
using FinanceManager.Web.ViewModels.Common;
using FinanceManager.Web.Services;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Shared.Dtos.Update;

namespace FinanceManager.Tests.ViewModels
{
    /// <summary>
    /// Covers <see cref="SetupCardViewModel"/>, the composite settings screen that hosts multiple setup
    /// sub-sections (profile, notifications, statements, return analysis, backup, and the admin-only update
    /// section) behind a single embedded panel and a single global Save/Reset ribbon action. Verifies section
    /// discovery and instantiation (including caching), admin-only section visibility, ribbon action
    /// aggregation across sections without action-id collisions, and that a global Save fans out to every
    /// dirty section's own save call.
    /// </summary>
    public class SetupCardViewModelTests
    {
        private sealed class TestCurrentUserService : FinanceManager.Application.ICurrentUserService
        {
            public TestCurrentUserService(Guid userId, bool isAuthenticated = true, bool isAdmin = false)
            {
                UserId = userId;
                IsAuthenticated = isAuthenticated;
                IsAdmin = isAdmin;
                PreferredLanguage = null;
            }

            public Guid UserId { get; }
            public string? PreferredLanguage { get; }
            public bool IsAuthenticated { get; }
            public bool IsAdmin { get; }
        }

        private static IServiceProvider BuildServices(bool isAdmin = false)
        {
            var sc = new ServiceCollection();
            sc.AddSingleton<FinanceManager.Application.ICurrentUserService>(new TestCurrentUserService(Guid.NewGuid(), isAdmin: isAdmin));
            sc.AddLogging();
            sc.AddSingleton<IKpiLocalStorageCache>(Mock.Of<IKpiLocalStorageCache>());
            sc.AddSingleton<IApiClient>(new Mock<IApiClient>().Object);
            return sc.BuildServiceProvider();
        }

        private static IServiceProvider BuildServices(IApiClient apiClient, bool isAdmin = false)
        {
            var sc = new ServiceCollection();
            sc.AddSingleton<FinanceManager.Application.ICurrentUserService>(new TestCurrentUserService(Guid.NewGuid(), isAdmin: isAdmin));
            sc.AddLogging();
            sc.AddSingleton<IKpiLocalStorageCache>(Mock.Of<IKpiLocalStorageCache>());
            sc.AddSingleton(apiClient);
            return sc.BuildServiceProvider();
        }

        /// <summary>
        /// Verifies that the setup card view model declaratively exposes a setup panel embedded panel spec
        /// positioned after the ribbon, with the inner component type and parameters passed through.
        /// </summary>
        [Fact]
        public void GetEmbeddedPanelSpecs_Returns_SetupPanel_AfterRibbon()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);

            var specs = vm.GetEmbeddedPanelSpecs(EmbeddedPanelPosition.AfterRibbon);

            specs.Should().ContainSingle();
            var spec = specs[0];
            spec.ComponentType.Should().Be(typeof(FinanceManager.Web.Components.Shared.SetupPanel));
            spec.Position.Should().Be(EmbeddedPanelPosition.AfterRibbon);

            // Outer parameters should include InnerComponentType and InnerParameters
            var parms = spec.Parameters as IDictionary<string, object>;
            parms.Should().NotBeNull();
            parms!.Should().ContainKey("InnerComponentType");
            parms.Should().ContainKey("InnerParameters");
        }

        /// <summary>
        /// Verifies that the "profile" section key resolves to the <c>SetupProfileTab</c> component type,
        /// part of the section registry the embedded panel uses to render the right tab content.
        /// </summary>
        [Fact]
        public void TryGetSectionComponentType_Profile_ReturnsExpectedComponent()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);

            var found = vm.TryGetSectionComponentType("profile", out var componentType);

            found.Should().BeTrue();
            componentType.Should().NotBeNull();
            componentType.Should().Be(typeof(FinanceManager.Web.Components.Pages.Setup.SetupProfileTab));
        }

        /// <summary>
        /// Verifies that the "profile" section key instantiates a <see cref="SetupProfileViewModel"/>,
        /// confirming the section-key-to-view-model-type mapping used when a tab is first opened.
        /// </summary>
        [Fact]
        public void CreateSectionViewModel_Profile_CreatesExpectedViewModel()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);

            var sectionVm = vm.CreateSectionViewModel("profile", sp);

            sectionVm.Should().NotBeNull();
            sectionVm.Should().BeOfType<SetupProfileViewModel>();
        }

        /// <summary>
        /// Verifies that a non-admin user does not see the "update" section at all: it is absent from
        /// <c>SettingSections</c>, its component type cannot be resolved, and its view model cannot be
        /// created - a security-relevant gate preventing non-admins from reaching update settings even by
        /// directly requesting the section key.
        /// </summary>
        [Fact]
        public async Task LoadAsync_HidesUpdateSectionForNonAdmin()
        {
            var sp = BuildServices(isAdmin: false);
            var vm = new SetupCardViewModel(sp);

            await vm.LoadAsync(Guid.Empty);

            vm.SettingSections.Select(section => section.Key).Should().NotContain("update");
            vm.TryGetSectionComponentType("update", out _).Should().BeFalse();
            vm.CreateSectionViewModel("update", sp).Should().BeNull();
        }

        /// <summary>
        /// Verifies that an admin user does see the "update" section, resolving to the
        /// <c>SetupUpdateTab</c> component - the counterpart to
        /// <see cref="LoadAsync_HidesUpdateSectionForNonAdmin"/> confirming the gate opens correctly for admins.
        /// </summary>
        [Fact]
        public async Task LoadAsync_ShowsUpdateSectionForAdmin()
        {
            var sp = BuildServices(isAdmin: true);
            var vm = new SetupCardViewModel(sp);

            await vm.LoadAsync(Guid.Empty);

            vm.SettingSections.Select(section => section.Key).Should().Contain("update");
            vm.TryGetSectionComponentType("update", out var componentType).Should().BeTrue();
            componentType.Should().Be(typeof(FinanceManager.Web.Components.Pages.Setup.SetupUpdateTab));
        }

        /// <summary>
        /// Verifies that after loading, the aggregated ribbon includes the backup section's own actions
        /// ("CreateBackup", "UploadBackup"), confirming that per-section ribbon contributions are merged
        /// into the card's overall ribbon.
        /// </summary>
        [Fact]
        public async Task GetRibbonRegisters_AfterLoad_IncludesBackupSectionActions()
        {
            var allActionIds = await GetAllActionIdsAfterLoad();

            allActionIds.Should().Contain("CreateBackup");
            allActionIds.Should().Contain("UploadBackup");
        }

        /// <summary>
        /// Verifies that once the admin-only update section is loaded, its actions ("UpdateCheckNow",
        /// "UpdateInstall", "UpdateResetLock") appear in the aggregated ribbon alongside the other sections'
        /// actions.
        /// </summary>
        [Fact]
        public async Task GetRibbonRegisters_AfterAdminLoad_IncludesUpdateActions()
        {
            var apiMock = new Mock<IApiClient>();
            apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false));
            apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateStatusDto(UpdateStatusKind.Ready, "1.0.0", null, "1.1.0", "win-x64", null, null, "release.zip", true, null, null, null));
            var sp = BuildServices(apiMock.Object, isAdmin: true);
            var vm = new SetupCardViewModel(sp);
            await vm.LoadAsync(Guid.Empty);
            var updateVm = (SetupUpdateViewModel)vm.CreateSectionViewModel("update", sp)!;
            await updateVm.LoadAsync(TestContext.Current.CancellationToken);

            var allActionIds = GetAllActionIds(vm);

            allActionIds.Should().Contain("UpdateCheckNow");
            allActionIds.Should().Contain("UpdateInstall");
            allActionIds.Should().Contain("UpdateResetLock");
        }

        /// <summary>
        /// Verifies the core "single global Save/Reset" design of the setup card: exactly one "Save" and
        /// one "Reset" action appear across the whole ribbon, even though multiple sections each have their
        /// own save/reset logic - individual sections must not leak their own per-section
        /// "SaveNotifications"/"ResetImportSplit"-style actions into the aggregated ribbon.
        /// </summary>
        [Fact]
        public async Task GetRibbonRegisters_AfterLoad_ExposesSingleGlobalSaveAndReset()
        {
            var allActionIds = await GetAllActionIdsAfterLoad();

            allActionIds.Count(id => id == "Save").Should().Be(1);
            allActionIds.Count(id => id == "Reset").Should().Be(1);
            allActionIds.Should().Contain("DetectTimezone");
            allActionIds.Should().NotContain("SaveNotifications");
            allActionIds.Should().NotContain("ResetNotifications");
            allActionIds.Should().NotContain("SaveImportSplit");
            allActionIds.Should().NotContain("ResetImportSplit");
        }

        private static async Task<List<string>> GetAllActionIdsAfterLoad()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);
            await vm.LoadAsync(Guid.Empty);
            return GetAllActionIds(vm);
        }

        /// <summary>
        /// Verifies that requesting the same section twice returns the same cached view model instance
        /// rather than constructing a new one each time - preserving in-progress edits/dirty state when a
        /// user switches away from a tab and back.
        /// </summary>
        [Fact]
        public async Task CreateSectionViewModel_AfterLoad_ReturnsCachedInstance()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);
            await vm.LoadAsync(Guid.Empty);

            var sectionVm = vm.CreateSectionViewModel("backup", sp);
            sectionVm.Should().NotBeNull();
            sectionVm.Should().BeOfType<SetupBackupsViewModel>();

            var sectionVm2 = vm.CreateSectionViewModel("backup", sp);
            sectionVm2.Should().BeSameAs(sectionVm);
        }

        /// <summary>
        /// Verifies that even if a section view model was created before <c>LoadAsync</c> ran (e.g. an
        /// eager tab pre-creation), loading still wires up the core global actions (Save, Reset,
        /// DetectTimezone) correctly - guarding against a load-order bug where an early-created section
        /// would end up excluded from the aggregated ribbon.
        /// </summary>
        [Fact]
        public async Task LoadAsync_InitializesCoreSections_WhenSectionWasCreatedBeforeLoad()
        {
            var sp = BuildServices();
            var vm = new SetupCardViewModel(sp);

            vm.CreateSectionViewModel("profile", sp);

            await vm.LoadAsync(Guid.Empty);

            var allActionIds = GetAllActionIds(vm);

            allActionIds.Should().Contain("Save");
            allActionIds.Should().Contain("Reset");
            allActionIds.Should().Contain("DetectTimezone");
        }

        /// <summary>
        /// Verifies the fan-out behavior of the global Save action: after making a change in each of five
        /// sections (profile, notifications, statements, return analysis, update), invoking the single
        /// "Save" ribbon action calls every dirty section's own update API exactly once - the mechanism
        /// that lets one Save button persist all pending changes across the whole settings screen.
        /// </summary>
        [Fact]
        public async Task GlobalSave_InvokesDirtySetupSectionSaves()
        {
            var apiMock = new Mock<IApiClient>();
            apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin" });
            apiMock.Setup(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            apiMock.Setup(a => a.User_GetNotificationSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationSettingsDto { HolidayProvider = "Memory" });
            apiMock.Setup(a => a.User_UpdateNotificationSettingsAsync(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            apiMock.Setup(a => a.UserSettings_GetImportSplitAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImportSplitSettingsDto { Mode = ImportSplitMode.FixedSize, MaxEntriesPerDraft = 20, MinEntriesPerDraft = 1 });
            apiMock.Setup(a => a.UserSettings_UpdateImportSplitAsync(It.IsAny<ImportSplitSettingsUpdateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            apiMock.Setup(a => a.Securities_ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SecurityDto>());
            apiMock.Setup(a => a.Securities_GetReturnAnalysisSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReturnAnalysisSettingsResponse(null, null, false, 0m));
            apiMock.Setup(a => a.Securities_UpdateReturnAnalysisSettingsAsync(It.IsAny<ReturnAnalysisSettingsUpdateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var updateSettings = new UpdateSettingsDto(false, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false);
            apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateSettings);
            apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateStatusDto(UpdateStatusKind.NoUpdate, "1.0.0", null, null, "win-x64", null, null, null, false, null, null, null));
            apiMock.Setup(a => a.Updates_UpdateSettingsAsync(It.IsAny<UpdateSettingsUpdateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateSettings with { Enabled = true });

            var sp = BuildServices(apiMock.Object, isAdmin: true);
            var vm = new SetupCardViewModel(sp);

            await vm.LoadAsync(Guid.Empty);

            var profileVm = (SetupProfileViewModel)vm.CreateSectionViewModel("profile", sp)!;
            await profileVm.LoadAsync(TestContext.Current.CancellationToken);
            profileVm.Model.PreferredLanguage = "en";
            profileVm.OnChanged();

            var notificationsVm = (SetupNotificationsViewModel)vm.CreateSectionViewModel("notifications", sp)!;
            await notificationsVm.LoadAsync(TestContext.Current.CancellationToken);
            notificationsVm.Model.MonthlyReminderEnabled = true;
            notificationsVm.OnChanged();

            var statementsVm = (SetupStatementsViewModel)vm.CreateSectionViewModel("statements", sp)!;
            await statementsVm.LoadAsync(TestContext.Current.CancellationToken);
            statementsVm.Model!.MaxEntriesPerDraft = 25;
            statementsVm.Validate();

            var returnAnalysisVm = (SetupReturnAnalysisViewModel)vm.CreateSectionViewModel("returnanalysis", sp)!;
            await returnAnalysisVm.LoadAsync(TestContext.Current.CancellationToken);
            returnAnalysisVm.ShowSharpeRatio = true;
            returnAnalysisVm.OnChanged();

            var updateVm = (SetupUpdateViewModel)vm.CreateSectionViewModel("update", sp)!;
            await updateVm.LoadAsync(TestContext.Current.CancellationToken);
            updateVm.UpdateSettings(updateSettings with { Enabled = true });

            var localizerMock = new Mock<IStringLocalizer>();
            localizerMock.Setup(l => l[It.IsAny<string>()])
                .Returns((string key) => new LocalizedString(key, key));

            var saveAction = vm.GetRibbonRegisters(localizerMock.Object)!
                .SelectMany(r => r.Tabs ?? new List<UiRibbonTab>())
                .SelectMany(t => t.Items)
                .First(a => a.Id == "Save");

            await saveAction.Callback!();

            apiMock.Verify(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            apiMock.Verify(a => a.User_UpdateNotificationSettingsAsync(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
            apiMock.Verify(a => a.UserSettings_UpdateImportSplitAsync(It.IsAny<ImportSplitSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            apiMock.Verify(a => a.Securities_UpdateReturnAnalysisSettingsAsync(It.IsAny<ReturnAnalysisSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            apiMock.Verify(a => a.Updates_UpdateSettingsAsync(It.IsAny<UpdateSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private static List<string> GetAllActionIds(SetupCardViewModel vm)
        {
            var localizerMock = new Mock<IStringLocalizer>();
            localizerMock.Setup(l => l[It.IsAny<string>()])
                .Returns((string key) => new LocalizedString(key, key));

            var registers = vm.GetRibbonRegisters(localizerMock.Object);

            registers.Should().NotBeNull();
            return registers!
                .SelectMany(r => r.Tabs ?? new List<UiRibbonTab>())
                .SelectMany(t => t.Items)
                .Select(a => a.Id)
                .ToList();
        }
    }
}
