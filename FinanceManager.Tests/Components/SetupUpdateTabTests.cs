using System.Net.Http;
using Bunit;
using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Update;
using FinanceManager.Web;
using FinanceManager.Web.Components.Pages.Setup;
using FinanceManager.Web.Services;
using FinanceManager.Web.ViewModels.Setup;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Tests for <see cref="SetupUpdateTab"/> and the update workflow it drives via
/// <see cref="SetupUpdateViewModel"/>: loading/localized status messages, hiding settings fields
/// and action buttons that don't apply to the current deployment mode, editing settings through the
/// form, the ribbon's reset-lock action staying enabled while a check is in progress, the
/// post-install health-check reload decision, and distinguishing a health-check timeout from a real
/// service outage.
/// </summary>
public sealed class SetupUpdateTabTests : BunitContext
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = true;
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The hanging handler should never complete a request.");
        }
    }

    private sealed class HangingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HangingHandler());
    }

    /// <summary>
    /// Verifies the pure decision rule <c>ShouldReloadAfterHealth</c>: a reload is only triggered
    /// when an outage was actually observed AND the subsequent health check succeeded - neither
    /// condition alone is sufficient, since reloading without a real outage is unnecessary and
    /// reloading despite a still-failing health check would just reload into a broken app.
    /// </summary>
    [Fact]
    public void ShouldReloadAfterHealth_RequiresObservedOutage()
    {
        SetupUpdateTab.ShouldReloadAfterHealth(outageObserved: false, healthSuccessful: true).Should().BeFalse();
        SetupUpdateTab.ShouldReloadAfterHealth(outageObserved: true, healthSuccessful: false).Should().BeFalse();
        SetupUpdateTab.ShouldReloadAfterHealth(outageObserved: true, healthSuccessful: true).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that while the settings and status API calls are still pending (never-completing
    /// tasks), the tab shows the localized "loading" message rather than an empty or broken view.
    /// </summary>
    [Fact]
    public void Render_WhileLoading_ShowsLocalizedLoadingMessage()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).Returns(new TaskCompletionSource<UpdateSettingsDto>().Task);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).Returns(new TaskCompletionSource<UpdateStatusDto>().Task);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));

        render.Markup.Should().Contain(localizer["Msg_Loading"].Value);
    }

    /// <summary>
    /// Verifies that once an install has started and the view-model has advanced to the
    /// "WaitingForRestart" install phase, the tab shows that phase's localized message - confirming
    /// the component's rendering tracks the view-model's fine-grained install phase, not just a
    /// coarse installing/not-installing flag.
    /// </summary>
    [Fact]
    public async Task Render_WhileInstallingAndWaitingPhase_ShowsLocalizedWaitingMessage()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false);
        var status = new UpdateStatusDto(UpdateStatusKind.Ready, "1.0.0", null, "1.0.1", "win-x64", null, null, "release.zip", false, null, null, null);
        var installing = status with { Status = UpdateStatusKind.Installing };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        apiMock.Setup(a => a.Updates_StartInstallAsync(It.IsAny<UpdateStartRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(installing);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);
        await vm.StartInstallAsync(confirmDowntime: true, ct: Xunit.TestContext.Current.CancellationToken);
        vm.SetInstallPhase("Msg_Update_WaitingForRestart");

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));

        render.Markup.Should().Contain(localizer["Msg_Update_WaitingForRestart"].Value);
    }

    /// <summary>
    /// Regression guard verifying that a set of settings fields and action buttons that were
    /// removed from this tab's UI (executable path, repository owner/name, manifest asset name,
    /// working directory, health timeout, and the save/check-now/install/reset-lock buttons) never
    /// render again, even though the loaded settings DTO still carries values for them - the DTO
    /// keeping the fields for compatibility must not cause the deprecated UI to reappear.
    /// </summary>
    [Fact]
    public void Render_WithLoadedSettings_HidesRemovedFieldsAndTabButtons()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, "FinanceManagerService", "app.exe", "updates", 120, false);
        var status = new UpdateStatusDto(UpdateStatusKind.Ready, "1.0.0", null, "1.0.1", "win-x64", null, null, "release.zip", false, null, null, null);
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));

        render.WaitForAssertion(() =>
        {
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_ExecutablePath"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_RepositoryOwner"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_RepositoryName"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_ManifestAssetName"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_WorkingDirectory"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Lbl_HealthTimeout"].Value);
            render.Markup.Should().NotContain("type=\"number\"");
            render.Markup.Should().NotContain(localizer["SetupUpdate_Btn_SaveSettings"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Btn_CheckNow"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Btn_Install"].Value);
            render.Markup.Should().NotContain(localizer["SetupUpdate_Btn_ResetLock"].Value);
        });
    }

    /// <summary>
    /// Verifies that the still-supported settings fields (include-prereleases checkbox and the
    /// source-check start/end time inputs) render, and that editing them through the DOM (toggling
    /// the checkbox, changing a time input) actually updates the bound view-model's
    /// <c>Settings</c> and flips <c>Dirty</c> to true - confirming two-way binding for the fields
    /// that remain editable on this tab.
    /// </summary>
    [Fact]
    public void Render_WithLoadedSettings_ShowsIncludePrereleasesCheckboxAndUpdatesViewModel()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false);
        var status = new UpdateStatusDto(UpdateStatusKind.NoUpdate, "1.0.0", null, null, "win-x64", null, null, null, false, null, null, null);
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));

        render.WaitForAssertion(() =>
        {
            render.Markup.Should().Contain(localizer["SetupUpdate_Lbl_IncludePrereleases"].Value);
            render.Markup.Should().Contain(localizer["SetupUpdate_Lbl_SourceCheckStartTime"].Value);
            render.Markup.Should().Contain(localizer["SetupUpdate_Lbl_SourceCheckEndTime"].Value);
            render.FindAll("input[type=checkbox]").Should().HaveCountGreaterThanOrEqualTo(2);
            render.FindAll("input[type=time]").Should().HaveCount(3);
        });

        render.FindAll("input[type=checkbox]")[1].Change(true);
        render.FindAll("input[type=time]")[0].Change("21:00");

        vm.Settings!.IncludePrereleases.Should().BeTrue();
        vm.Settings.SourceCheckStartTime.Should().Be(new TimeOnly(21, 0));
        vm.Dirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the update status enum value is rendered through its localized resource string
    /// (e.g. "UpdateStatusKind_Ready") rather than the raw enum name appearing in the markup, so
    /// users never see an untranslated technical status value.
    /// </summary>
    [Fact]
    public void Render_WithLoadedStatus_ShowsLocalizedStatus()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false);
        var status = new UpdateStatusDto(UpdateStatusKind.Ready, "1.0.0", null, "1.0.1", "win-x64", null, null, "release.zip", false, null, null, null);
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));

        render.WaitForAssertion(() =>
        {
            render.Markup.Should().Contain(localizer["UpdateStatusKind_Ready"].Value);
            render.Markup.Should().NotContain($">{UpdateStatusKind.Ready}<");
        });
    }

    /// <summary>
    /// Verifies that while the view-model is busy running a check (an update lock is held from two
    /// hours ago), the ribbon's "UpdateResetLock" action remains enabled rather than being disabled
    /// along with the other busy-gated actions - an admin must always be able to reset a stuck lock
    /// even while a check is in flight, otherwise a genuinely stuck lock could become unrecoverable
    /// through the UI.
    /// </summary>
    [Fact]
    public async Task Ribbon_WhenBusyAndUpdateLockActive_DoesNotDisableResetLockAction()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 120, false);
        var status = new UpdateStatusDto(UpdateStatusKind.NoUpdate, "1.0.0", null, null, "win-x64", null, null, null, true, DateTimeOffset.UtcNow.AddHours(-2), null, null);
        var checkTask = new TaskCompletionSource<UpdateCheckResultDto>();
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        apiMock.Setup(a => a.Updates_CheckAsync(It.IsAny<CancellationToken>())).Returns(checkTask.Task);
        var (vm, localizer) = CreateVmAndLocalizer(apiMock.Object);
        await vm.LoadAsync(Xunit.TestContext.Current.CancellationToken);

        var busyTask = vm.CheckAsync(Xunit.TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!vm.Busy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Xunit.TestContext.Current.CancellationToken);
        }

        vm.Busy.Should().BeTrue();
        var resetLockAction = vm.GetRibbon(localizer)!
            .SelectMany(register => register.Items)
            .Single(action => action.Id == "UpdateResetLock");

        resetLockAction.Disabled.Should().BeFalse();

        checkTask.SetResult(new UpdateCheckResultDto(false, status, null));
        await busyTask;
    }

    private (SetupUpdateViewModel Vm, IStringLocalizer<Pages> Localizer) CreateVmAndLocalizer(IApiClient api, bool useHangingHttpClient = false)
    {
        Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        Services.AddSingleton(api);
        if (useHangingHttpClient)
        {
            Services.AddSingleton<IHttpClientFactory>(new HangingHttpClientFactory());
        }
        else
        {
            Services.AddHttpClient();
        }
        Services.AddLocalization(options => options.ResourcesPath = "Resources");
        Services.AddSingleton(typeof(IStringLocalizer<Pages>), new PagesStringLocalizer());
        var confirmationMock = new Mock<IConfirmationService>();
        confirmationMock.Setup(c => c.ConfirmAsync(It.IsAny<ConfirmationRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Services.AddSingleton(confirmationMock.Object);
        var sp = Services.BuildServiceProvider();
        var vm = new SetupUpdateViewModel(sp);
        return (vm, sp.GetRequiredService<IStringLocalizer<Pages>>());
    }

    /// <summary>
    /// Verifies that when the post-install health check hangs and is cancelled by its own timeout
    /// (simulated with an <see cref="HttpMessageHandler"/> that never completes), the view-model
    /// reports the specific <c>Err_Update_HealthTimeout</c> error and stays in the
    /// <c>Msg_Update_Installing</c> phase, rather than misclassifying a local timeout as a detected
    /// service outage - the two failure modes need different handling and must not be conflated.
    /// </summary>
    [Fact]
    public async Task PollHealthAsync_WhenHealthCheckIsCancelledByTimeout_DoesNotTreatCancellationAsOutage()
    {
        var settings = new UpdateSettingsDto(true, "owner", "repo", "update.json", new TimeOnly(20, 0), new TimeOnly(6, 0), null, null, null, "updates", 3, false);
        var status = new UpdateStatusDto(UpdateStatusKind.Ready, "1.0.0", null, "1.0.1", "win-x64", null, null, "release.zip", false, null, null, null);
        var installing = status with { Status = UpdateStatusKind.Installing };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.Updates_GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        apiMock.Setup(a => a.Updates_GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
        apiMock.Setup(a => a.Updates_StartInstallAsync(It.IsAny<UpdateStartRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(installing);
        var (vm, _) = CreateVmAndLocalizer(apiMock.Object, useHangingHttpClient: true);
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);

        var render = Render<SetupUpdateTab>(parameters => parameters.Add(p => p.ViewModel, vm));
        await vm.StartInstallWithConfirmationAsync(Xunit.TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (vm.LastErrorCode is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        }

        vm.LastErrorCode.Should().Be("Err_Update_HealthTimeout");
        vm.InstallPhase.Should().Be("Msg_Update_Installing");
    }
}
