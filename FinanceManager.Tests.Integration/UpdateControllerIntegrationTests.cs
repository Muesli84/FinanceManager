using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Update;
using FinanceManager.Tests.Updates;
using FinanceManager.Web.Services.Updates;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using msTools.Updater;
using Xunit;

namespace FinanceManager.Tests.Integration;

/// <summary>
/// End-to-end tests for the self-update API surface (<c>/api/setup/update/*</c>): admin authorization,
/// settings round-tripping, install/lock lifecycle error classification, and how persisted settings and
/// on-disk status/version state are reconciled after a process restart.
/// </summary>
public sealed class UpdateControllerIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateControllerIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">Shared web application factory providing the in-memory test server.</param>
    public UpdateControllerIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies that the health endpoint stays reachable without authentication, since external
    /// monitoring/orchestration tooling probes it before any credentials are available.
    /// </summary>
    [Fact]
    public async Task Health_IsAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that update status is not exposed to unauthenticated callers, since it can reveal
    /// installed version and update-progress details that should be admin-only.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_RequiresAdmin()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/setup/update/status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that update settings saved by an admin through the API are persisted and returned back
    /// unchanged, so the setup UI can rely on a save-then-reload round trip.
    /// </summary>
    [Fact]
    public async Task UpdateSettings_RoundTripsForAdmin()
    {
        var client = _factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var update = new UpdateSettingsUpdateRequest(
            true,
            "martin-stromberg",
            "FinanceManager",
            "update.json",
            new TimeOnly(20, 0),
            new TimeOnly(6, 0),
            new TimeOnly(3, 30),
            "FinanceManagerService",
            null,
            "updates",
            120,
            true);

        var put = await client.PutAsJsonAsync("/api/setup/update/settings", update, cancellationToken: TestContext.Current.CancellationToken);
        put.EnsureSuccessStatusCode();
        var settings = await put.Content.ReadFromJsonAsync<UpdateSettingsDto>(cancellationToken: TestContext.Current.CancellationToken);

        settings!.Enabled.Should().BeTrue();
        settings.SourceCheckStartTime.Should().Be(new TimeOnly(20, 0));
        settings.SourceCheckEndTime.Should().Be(new TimeOnly(6, 0));
        settings.RepositoryOwner.Should().Be("martin-stromberg");
        settings.IncludePrereleases.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that starting an install while another update lock is already active surfaces as an HTTP
    /// 409 with the localizable "Err_Update_Locked" error code, instead of an unclassified server error.
    /// </summary>
    [Fact]
    public async Task StartInstall_ReturnsConflict_WhenUpdateLockIsActive()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdateOrchestrator>();
                services.AddScoped<IUpdateOrchestrator>(_ => new ThrowingUpdateOrchestrator(new IOException("An update lock is active.")));
            });
        });
        var client = factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/setup/update/install/start", new UpdateStartRequest(true), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
        error!.code.Should().Be("Err_Update_Locked");
    }

    /// <summary>
    /// Verifies that starting an install with no downloaded package ready surfaces as an HTTP 404 with the
    /// localizable "Err_Update_NotReady" error code, so the setup UI can show an actionable message
    /// instead of a generic failure.
    /// </summary>
    [Fact]
    public async Task StartInstall_ReturnsNotFoundWithLocalizableCode_WhenNoReadyPackage()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdateOrchestrator>();
                services.AddScoped<IUpdateOrchestrator>(_ => new ThrowingUpdateOrchestrator(new FileNotFoundException("No ready update package is available.")));
            });
        });
        var client = factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/setup/update/install/start", new UpdateStartRequest(true), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
        error!.code.Should().Be("Err_Update_NotReady");
    }

    /// <summary>
    /// Verifies that resetting a stale update lock (older than the staleness threshold) succeeds with an
    /// HTTP 204 and actually removes the lock file on disk, unblocking a subsequent install attempt.
    /// </summary>
    [Fact]
    public async Task ResetLock_Returns204_WhenStaleLockIsReleasedOnDisk()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => SetDownloadPath(services, tempDir.FullName));
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);

            var lockPath = Path.Combine(tempDir.FullName, "update.lock");
            await File.WriteAllTextAsync(lockPath, DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"), TestContext.Current.CancellationToken);

            var response = await client.PostAsJsonAsync("/api/setup/update/lock/reset", new UpdateLockResetRequest("integration test"), cancellationToken: TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(lockPath).Should().BeFalse();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that each classified <see cref="UpdateLockResetException"/> kind the orchestrator can throw
    /// is translated to its own distinct HTTP status and localizable error code, rather than all collapsing
    /// into one generic reset-failure response the setup UI could not distinguish between.
    /// </summary>
    /// <param name="kind">The classified reset-failure kind the orchestrator throws.</param>
    /// <param name="statusCode">The HTTP status code expected for that failure kind.</param>
    /// <param name="errorCode">The localizable error code expected in the response body for that failure kind.</param>
    [Theory]
    [InlineData(UpdateLockResetFailureKind.NoLock, HttpStatusCode.Conflict, "Err_Update_Reset_NoLock")]
    [InlineData(UpdateLockResetFailureKind.LockNotStale, HttpStatusCode.Conflict, "Err_Update_Reset_LockNotStale")]
    [InlineData(UpdateLockResetFailureKind.LockDeleteFailed, HttpStatusCode.Conflict, "Err_Update_Reset_DeleteFailed")]
    [InlineData(UpdateLockResetFailureKind.ResetFailed, HttpStatusCode.InternalServerError, "Err_Update_Reset_Failed")]
    public async Task ResetLock_ReturnsSpecificErrorCode_WhenResetFailureIsClassified(
        UpdateLockResetFailureKind kind,
        HttpStatusCode statusCode,
        string errorCode)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdateOrchestrator>();
                services.AddScoped<IUpdateOrchestrator>(_ => new ThrowingUpdateOrchestrator(
                    new NotSupportedException(),
                    new UpdateLockResetException(kind, UpdateLockResetFailureSource.FinanceManager, "reset failed")));
            });
        });
        var client = factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/setup/update/lock/reset", new UpdateLockResetRequest("integration test"), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(statusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
        error!.code.Should().Be(errorCode);
        error.code.Should().NotBe("Err_Update_InstallRunning");
    }

    /// <summary>
    /// Verifies that an unclassified <see cref="IOException"/> raised while resetting the lock still falls
    /// back to the "Err_Update_Reset_Failed" error code, so an unexpected filesystem error never surfaces
    /// as an unhandled 500 with no actionable code for the setup UI.
    /// </summary>
    [Fact]
    public async Task ResetLock_ReturnsResetFailed_WhenResetThrowsUnclassifiedIOException()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdateOrchestrator>();
                services.AddScoped<IUpdateOrchestrator>(_ => new ThrowingUpdateOrchestrator(
                    new NotSupportedException(),
                    new IOException("unclassified reset failure")));
            });
        });
        var client = factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/setup/update/lock/reset", new UpdateLockResetRequest("integration test"), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
        error!.code.Should().Be("Err_Update_Reset_Failed");
    }

    /// <summary>
    /// Verifies that resetting the lock when no lock file exists on disk at all returns a 409 conflict with
    /// "Err_Update_Reset_NoLock", instead of silently succeeding or erroring on a missing file.
    /// </summary>
    [Fact]
    public async Task ResetLock_Returns409NoLock_WhenNoLockFileExists()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => SetDownloadPath(services, tempDir.FullName));
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);

            var response = await client.PostAsJsonAsync("/api/setup/update/lock/reset", new UpdateLockResetRequest("integration test"), cancellationToken: TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
            error!.code.Should().Be("Err_Update_Reset_NoLock");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that resetting a lock that is still fresh (younger than the staleness threshold) is
    /// rejected with "Err_Update_Reset_LockNotStale" and, crucially, leaves the lock file untouched on
    /// disk - a reset must never remove a lock that might still guard an in-progress install.
    /// </summary>
    [Fact]
    public async Task ResetLock_Returns409LockNotStale_WhenLockFileIsTooYoung()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => SetDownloadPath(services, tempDir.FullName));
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);

            var lockPath = Path.Combine(tempDir.FullName, "update.lock");
            await File.WriteAllTextAsync(lockPath, DateTimeOffset.UtcNow.ToString("O"), TestContext.Current.CancellationToken);

            var response = await client.PostAsJsonAsync("/api/setup/update/lock/reset", new UpdateLockResetRequest("integration test"), cancellationToken: TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: TestContext.Current.CancellationToken);
            error!.code.Should().Be("Err_Update_Reset_LockNotStale");
            File.Exists(lockPath).Should().BeTrue();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a successful install still leaves the update lock file on disk when the installer
    /// itself does not clean it up (e.g. because a service restart is expected to end the process), so
    /// the lock's lifecycle owner is the orchestrator/installer contract, not an implicit controller
    /// cleanup step.
    /// </summary>
    [Fact]
    public async Task StartInstall_SucceedsAndLockRemains_WhenInstallerDoesNotCleanUpLock()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    SetDownloadPath(services, tempDir.FullName);
                    services.RemoveAll<IAutoUpdateOrchestrator>();
                    services.AddSingleton<IAutoUpdateOrchestrator>(new SucceedingAutoUpdateOrchestrator());
                });
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);

            var lockPath = Path.Combine(tempDir.FullName, "update.lock");
            await File.WriteAllTextAsync(lockPath, DateTimeOffset.UtcNow.ToString("O"), TestContext.Current.CancellationToken);

            var response = await client.PostAsJsonAsync("/api/setup/update/install/start", new UpdateStartRequest(true), cancellationToken: TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            File.Exists(lockPath).Should().BeTrue();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that update settings persisted to disk by a previous run are re-applied to the
    /// auto-update library's live <see cref="AutoUpdateOptions"/> on the very next application startup,
    /// without requiring the admin to re-save them through the UI - guarding against a startup path that
    /// only reads persisted settings when they are explicitly re-submitted.
    /// </summary>
    [Fact]
    public async Task PersistedSettings_AreAppliedToAutoUpdateOptions_OnStartup_WithoutManualSave()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // Simulates settings saved through the setup UI during a previous run, persisted to disk, but never
            // re-applied to the auto-update library's runtime options because the process later restarted.
            var persisted = new UpdateSettingsDto(
                true,
                "martin-stromberg",
                "FinanceManager",
                "update.json",
                new TimeOnly(21, 0),
                new TimeOnly(5, 0),
                null,
                "PersistedServiceName",
                null,
                tempDir.FullName,
                250,
                true);
            var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "settings.json"), json, TestContext.Current.CancellationToken);

            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => SetDownloadPath(services, tempDir.FullName));
            });
            using var client = factory.CreateClient();

            var options = factory.Services.GetRequiredService<AutoUpdateOptions>();

            options.ServiceName.Should().Be("PersistedServiceName");
            options.SourceCheck.Interval.Should().Be(AutoUpdateOptionsMapper.DailySourceCheckIntervalMinutes);
            options.SourceCheck.TimeRanges.Should().HaveCount(14);
            options.HealthTimeoutSeconds.Should().Be(250);
            options.AllowPrereleaseUpdates.Should().BeTrue();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that when the process restarts mid-install and the installed version already matches the
    /// version that was being installed, status reconciliation reports "NoUpdate" rather than getting
    /// stuck reporting a stale "Installing" state forever.
    /// </summary>
    [Fact]
    public async Task Status_WhenInstallingAndVersionMatchesAfterRestart_ReportsNoUpdate()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    SetDownloadPath(services, tempDir.FullName);
                    services.RemoveAll<IInstalledReleaseMetadataProvider>();
                    services.AddSingleton<IInstalledReleaseMetadataProvider>(new FixedInstalledReleaseMetadataProvider("1.2.3"));
                    services.RemoveAll<IInstalledVersionProvider>();
                    services.AddSingleton<IInstalledVersionProvider>(new FixedInstalledVersionProvider("1.2.3"));
                });
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);
            await WriteStatusAsync(tempDir.FullName, UpdateStatusTestData.InstallingSnapshot("1.2.3"));

            var response = await client.GetAsync("/api/setup/update/status", TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<UpdateStatusDto>(cancellationToken: TestContext.Current.CancellationToken);
            status!.Status.Should().Be(UpdateStatusKind.NoUpdate);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that when the process restarts mid-install but the installed version does not match the
    /// version that was being installed, status reconciliation reports "Failed" with a specific,
    /// human-readable error describing the version mismatch - so a botched install is surfaced instead of
    /// silently being reported as up to date or left "Installing" indefinitely.
    /// </summary>
    [Fact]
    public async Task Status_WhenInstallingAndVersionMismatchAfterRestart_ReportsFailed()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    SetDownloadPath(services, tempDir.FullName);
                    services.RemoveAll<IInstalledReleaseMetadataProvider>();
                    services.AddSingleton<IInstalledReleaseMetadataProvider>(new FixedInstalledReleaseMetadataProvider("1.2.3"));
                    services.RemoveAll<IInstalledVersionProvider>();
                    services.AddSingleton<IInstalledVersionProvider>(new FixedInstalledVersionProvider("1.2.3"));
                });
            });
            var client = factory.CreateClient();
            await AuthenticateAdminAsync(client);
            await WriteStatusAsync(tempDir.FullName, UpdateStatusTestData.InstallingSnapshot("9.9.9"));

            var response = await client.GetAsync("/api/setup/update/status", TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<UpdateStatusDto>(cancellationToken: TestContext.Current.CancellationToken);
            status!.Status.Should().Be(UpdateStatusKind.Failed);
            status.LastError.Should().Be("Installed version '1.2.3' does not match the expected version '9.9.9' after the update process finished.");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies that starting an install without confirming the required downtime surfaces as an HTTP 400,
    /// so the setup UI is forced to obtain explicit confirmation before the service can be taken down.
    /// </summary>
    [Fact]
    public async Task StartInstall_ReturnsBadRequest_WhenDowntimeIsNotConfirmed()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdateOrchestrator>();
                services.AddScoped<IUpdateOrchestrator>(_ => new ThrowingUpdateOrchestrator(new ArgumentException("Downtime confirmation is required.", "confirmDowntime")));
            });
        });
        var client = factory.CreateClient();
        await AuthenticateAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/setup/update/install/start", new UpdateStartRequest(false), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Task WriteStatusAsync(string downloadPath, AutoUpdateStatusSnapshot snapshot)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Directory.CreateDirectory(downloadPath);
        return File.WriteAllTextAsync(Path.Combine(downloadPath, "status.json"), JsonSerializer.Serialize(snapshot, options));
    }

    /// <summary>
    /// Mutates <see cref="AutoUpdateOptions.DownloadPath"/> on the already-registered singleton instance before the
    /// test server starts, so status/lock files are read from an isolated temp directory instead of the real
    /// <c>updates</c> folder.
    /// </summary>
    /// <param name="services">The service collection to locate the registered <see cref="AutoUpdateOptions"/> instance in.</param>
    /// <param name="downloadPath">The temporary directory to redirect <see cref="AutoUpdateOptions.DownloadPath"/> to.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown with an actionable message if <see cref="AutoUpdateOptions"/> is no longer registered as a singleton
    /// instance descriptor (see <c>AutoUpdateHostBuilderExtensions.UseAutoUpdate</c>), instead of failing later with
    /// an unexplained <see cref="NullReferenceException"/>.
    /// </exception>
    private static void SetDownloadPath(IServiceCollection services, string downloadPath)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AutoUpdateOptions));
        if (descriptor?.ImplementationInstance is not AutoUpdateOptions options)
        {
            throw new InvalidOperationException(
                $"Expected {nameof(AutoUpdateOptions)} to be registered as a singleton instance " +
                "(see AutoUpdateHostBuilderExtensions.UseAutoUpdate's `builder.Services.AddSingleton(options)`), " +
                "so this test helper can mutate DownloadPath before the test server starts. " +
                "The registration style has changed - update this helper to match.");
        }

        options.DownloadPath = downloadPath;
    }

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = TestWebApplicationFactory.BootstrapAdminUsername,
            password = TestWebApplicationFactory.BootstrapAdminPassword
        });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Admin login failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }

    private sealed class ThrowingUpdateOrchestrator : IUpdateOrchestrator
    {
        private readonly Exception _startException;
        private readonly Exception? _resetException;

        public ThrowingUpdateOrchestrator(Exception startException, Exception? resetException = null)
        {
            _startException = startException;
            _resetException = resetException;
        }

        public Task<UpdateStatusDto> GetStatusAsync(CancellationToken ct = default) => Task.FromException<UpdateStatusDto>(new NotSupportedException());
        public Task<UpdateSettingsDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromException<UpdateSettingsDto>(new NotSupportedException());
        public Task<UpdateSettingsDto> SaveSettingsAsync(UpdateSettingsUpdateRequest request, CancellationToken ct = default) => Task.FromException<UpdateSettingsDto>(new NotSupportedException());
        public Task<UpdateSettingsDto> ScheduleAsync(TimeOnly? scheduledInstallTime, CancellationToken ct = default) => Task.FromException<UpdateSettingsDto>(new NotSupportedException());
        public Task<UpdateCheckResultDto> CheckAsync(CancellationToken ct = default) => Task.FromException<UpdateCheckResultDto>(new NotSupportedException());
        public Task ResetLockAsync(string? reason, CancellationToken ct = default)
            => _resetException is null ? throw new NotSupportedException() : Task.FromException(_resetException);

        public Task<UpdateStatusDto> StartInstallAsync(bool confirmDowntime, CancellationToken ct = default) => Task.FromException<UpdateStatusDto>(_startException);
    }

    private sealed class SucceedingAutoUpdateOrchestrator : IAutoUpdateOrchestrator
    {
        public Task<AutoUpdateResult> RunUpdateAsync(CancellationToken ct = default) => Task.FromException<AutoUpdateResult>(new NotSupportedException());
        public Task<AutoUpdateResult> CheckForUpdateAsync(CancellationToken ct = default) => Task.FromException<AutoUpdateResult>(new NotSupportedException());
        public Task<AutoUpdateResult> DownloadAsync(CancellationToken ct = default) => Task.FromException<AutoUpdateResult>(new NotSupportedException());
        public Task<AutoUpdateResult> InstallAsync(bool confirmDowntime, bool force, CancellationToken ct = default)
            => Task.FromResult(new AutoUpdateResult(AutoUpdateOutcome.Success, AutoUpdateState.Success, "installed", null));
        public Task<AutoUpdateStatusSnapshot> GetStatusAsync(CancellationToken ct = default) => Task.FromException<AutoUpdateStatusSnapshot>(new NotSupportedException());
    }

    private sealed class FixedInstalledReleaseMetadataProvider : IInstalledReleaseMetadataProvider
    {
        private readonly string _version;

        public FixedInstalledReleaseMetadataProvider(string version)
        {
            _version = version;
        }

        public Task<InstalledReleaseMetadataDto> GetAsync(CancellationToken ct = default)
            => Task.FromResult(new InstalledReleaseMetadataDto(_version, null, null, null, null));
    }

    private sealed class FixedInstalledVersionProvider : IInstalledVersionProvider
    {
        private readonly string _version;

        public FixedInstalledVersionProvider(string version)
        {
            _version = version;
        }

        public Task<InstalledReleaseInfo> GetAsync(CancellationToken ct = default)
            => Task.FromResult(new InstalledReleaseInfo(_version, null, null, null, null));
    }
}
