using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// xUnit fixture that, once per test collection, builds and starts a real instance of FinanceManager.Web
/// against a throwaway SQLite database and a seeded local-folder update source, launches a Playwright
/// browser against it, and tears everything down afterwards - so E2E tests exercise the actual application
/// end to end instead of a simulated host.
/// </summary>
public sealed class PlaywrightWebAppFixture : IAsyncLifetime
{
    /// <summary>
    /// Optional per-session browser context overrides (viewport, mobile emulation, locale) passed to
    /// <see cref="CreateSessionAsync"/>, so individual tests can opt into e.g. a mobile viewport without
    /// needing a separate fixture or server.
    /// </summary>
    public sealed class PlaywrightSessionOptions
    {
        /// <summary>The viewport size to use for the session's browser context, or <see langword="null"/> to use Playwright's default.</summary>
        public ViewportSize? ViewportSize { get; init; }

        /// <summary>Whether to emulate a mobile device for the session's browser context, or <see langword="null"/> to use Playwright's default.</summary>
        public bool? IsMobile { get; init; }

        /// <summary>Whether to emulate touch input for the session's browser context, or <see langword="null"/> to use Playwright's default.</summary>
        public bool? HasTouch { get; init; }

        /// <summary>The locale to emulate for the session's browser context, or <see langword="null"/> to use Playwright's default.</summary>
        public string? Locale { get; init; }
    }

    /// <summary>
    /// Version advertised by the local-folder update source seeded for this fixture, always newer than
    /// <see cref="InstalledUpdateVersion"/> so a manual check reports an available update.
    /// </summary>
    public const string AvailableUpdateVersion = "9.9.9";

    /// <summary>
    /// Installed version seeded via <c>release-metadata.json</c> for this fixture's server.
    /// </summary>
    public const string InstalledUpdateVersion = "1.0.0";

    private static readonly PlaywrightSessionOptions MobileSessionOptions = new()
    {
        ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        IsMobile = true,
        HasTouch = true
    };

    private readonly PlaywrightTestOptions _options = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Process? _server;
    private string? _baseUrl;
    private string? _dbPath;
    private string? _updatesSourceDir;
    private string? _updatesWorkingDir;
    private string? _accountStatisticsFaultFile;
    private string? _releaseMetadataPath;
    private string? _originalReleaseMetadata;
    private EventHandler? _processExitHandler;
    private readonly StringBuilder _serverOutput = new();
    private readonly StringBuilder _serverError = new();

    /// <summary>
    /// The base URL of the running test server (e.g. <c>https://127.0.0.1:{port}</c>), for navigating to
    /// pages under test.
    /// </summary>
    public string BaseUrl => _baseUrl ?? throw new InvalidOperationException("The Playwright server is not initialized.");

    /// <summary>
    /// Filesystem path to the SQLite database backing the running test server, for seeding data directly
    /// (see <see cref="TestUserSeeder"/>) without going through the UI.
    /// </summary>
    public string DatabasePath => _dbPath ?? throw new InvalidOperationException("The Playwright database is not initialized.");

    /// <summary>
    /// Starts the test server - against a fresh temporary SQLite database and a seeded local-folder update
    /// source - and launches the Playwright browser used to create sessions against it. Invoked once by
    /// xUnit before any test in the collection runs.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        var port = GetFreePort();
        _baseUrl = $"https://127.0.0.1:{port}";
        _dbPath = Path.Combine(Path.GetTempPath(), $"financemanager-e2e-{Guid.NewGuid():N}.db");
        _updatesSourceDir = Path.Combine(Path.GetTempPath(), $"financemanager-e2e-update-source-{Guid.NewGuid():N}");
        _updatesWorkingDir = Path.Combine(Path.GetTempPath(), $"financemanager-e2e-update-working-{Guid.NewGuid():N}");
        _accountStatisticsFaultFile = Path.Combine(Path.GetTempPath(), $"financemanager-e2e-account-statistics-fault-{Guid.NewGuid():N}.flag");

        var webDll = ResolveWebDllPath();
        PrepareUpdateSource(_updatesSourceDir);
        PrepareInstalledReleaseMetadata();
        _processExitHandler = (_, _) => RestoreInstalledReleaseMetadata();
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        StartServer(port, webDll, _dbPath);
        await WaitForServerAsync();

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await LaunchBrowserAsync(_playwright);
    }

    /// <summary>
    /// Shuts down the browser and the test server, deletes the temporary database, and restores any
    /// <c>release-metadata.json</c> content that was overwritten for the test run. Invoked once by xUnit
    /// after all tests in the collection have run.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch
            {
                // Best-effort cleanup only.
            }

            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        if (_server != null)
        {
            try
            {
                if (!_server.HasExited)
                {
                    _server.Kill(entireProcessTree: true);
                    await _server.WaitForExitAsync();
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            _server.Dispose();
            _server = null;
        }

        if (!string.IsNullOrWhiteSpace(_dbPath) && File.Exists(_dbPath))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(_dbPath);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(200);
                }
                catch
                {
                    // Best-effort cleanup only.
                    break;
                }
            }
        }

        if (_processExitHandler is not null)
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
            _processExitHandler = null;
        }

        RestoreInstalledReleaseMetadata();
        DeleteDirectoryBestEffort(_updatesSourceDir);
        DeleteDirectoryBestEffort(_updatesWorkingDir);
        ResetAccountStatisticsFault();
    }

    /// <summary>
    /// Creates a new, isolated browser context and page against the running test server, applying the
    /// configured action/navigation timeouts and, if enabled, trace recording - so each test gets its own
    /// cookies and storage without needing a separate browser instance per test.
    /// </summary>
    /// <param name="options">Optional per-session context overrides such as viewport size or mobile emulation, or <see langword="null"/> to use Playwright's defaults.</param>
    /// <returns>A task that resolves to the new <see cref="PlaywrightBrowserSession"/>.</returns>
    public async Task<PlaywrightBrowserSession> CreateSessionAsync(PlaywrightSessionOptions? options = null)
    {
        if (_browser == null)
        {
            throw new InvalidOperationException("Browser is not initialized.");
        }

        ResetAccountStatisticsFault();

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = options?.ViewportSize,
            IsMobile = options?.IsMobile,
            HasTouch = options?.HasTouch,
            Locale = options?.Locale,
        });
        context.SetDefaultTimeout(_options.ActionTimeoutSeconds * 1000);
        context.SetDefaultNavigationTimeout(_options.NavigationTimeoutSeconds * 1000);

        var artifactPrefix = (_options.ArtifactCaptureEnabled || _options.TraceEnabled)
            ? Path.Combine(GetArtifactDirectory(), $"session-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}")
            : null;
        if (_options.TraceEnabled)
        {
            await context.Tracing.StartAsync(new()
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
        }

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(_options.ActionTimeoutSeconds * 1000);
        page.SetDefaultNavigationTimeout(_options.NavigationTimeoutSeconds * 1000);
        return new PlaywrightBrowserSession(context, page, artifactPrefix, _options.ArtifactCaptureEnabled, _options.TraceEnabled, () =>
        {
            ResetAccountStatisticsFault();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Enables a test-only fault for <c>GET /api/accounts/statistics</c> in the running E2E server.
    /// </summary>
    public void EnableAccountStatisticsFault()
    {
        if (string.IsNullOrWhiteSpace(_accountStatisticsFaultFile))
        {
            throw new InvalidOperationException("The Playwright server is not initialized.");
        }

        File.WriteAllText(_accountStatisticsFaultFile, "fail");
    }

    /// <summary>
    /// Disables the test-only account statistics fault.
    /// </summary>
    public void ResetAccountStatisticsFault()
    {
        if (string.IsNullOrWhiteSpace(_accountStatisticsFaultFile) || !File.Exists(_accountStatisticsFaultFile))
        {
            return;
        }

        File.Delete(_accountStatisticsFaultFile);
    }

    /// <summary>
    /// Creates a new session pre-configured to emulate a typical mobile phone viewport with touch support,
    /// for tests that need to verify mobile-specific layout or behavior.
    /// </summary>
    /// <returns>A task that resolves to the new <see cref="PlaywrightBrowserSession"/>.</returns>
    public Task<PlaywrightBrowserSession> CreateMobileSessionAsync()
        => CreateSessionAsync(MobileSessionOptions);

    private async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var opts = new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless,
        };

        if (!string.IsNullOrWhiteSpace(_options.BrowserChannel))
        {
            opts.Channel = _options.BrowserChannel;
        }

        try
        {
            return await playwright.Chromium.LaunchAsync(opts);
        }
        catch
        {
            if (!string.Equals(_options.BrowserChannel, "chromium", StringComparison.OrdinalIgnoreCase))
            {
                opts.Channel = null;
                return await playwright.Chromium.LaunchAsync(opts);
            }

            throw;
        }
    }

    private void StartServer(int port, string webDll, string dbPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{webDll}\"",
            WorkingDirectory = Path.Combine(GetRepoRoot(), "FinanceManager.Web"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = $"https://127.0.0.1:{port};http://127.0.0.1:{port + 1}";
        startInfo.Environment["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{port + 1}";
        startInfo.Environment["Kestrel__Endpoints__Https__Url"] = $"https://127.0.0.1:{port}";
        startInfo.Environment["Api__BaseAddress"] = $"http://127.0.0.1:{port + 1}/";
        startInfo.Environment["E2E__DisableHttpsRedirection"] = "true";
        startInfo.Environment["E2E__AccountStatisticsFaultInjectionEnabled"] = "true";
        startInfo.Environment["E2E__AccountStatisticsFaultFile"] = _accountStatisticsFaultFile;
        startInfo.Environment["ConnectionStrings__Default"] = $"Data Source={dbPath}";
        startInfo.Environment["BackgroundTasks__Enabled"] = "false";
        startInfo.Environment["Workers__SecurityPriceWorker__Enabled"] = "false";
        startInfo.Environment["FileLogging__Enabled"] = "false";
        startInfo.Environment["DetailedErrors"] = "true";
        startInfo.Environment["Updates__Enabled"] = "true";
        startInfo.Environment["Updates__SourceType"] = "LocalFolder";
        startInfo.Environment["Updates__LocalFolderPath"] = _updatesSourceDir;
        startInfo.Environment["Updates__EnableAutomaticInstallation"] = "false";
        startInfo.Environment["Updates__HostedServicesEnabled"] = "false";
        startInfo.Environment["Updates__WorkingDirectory"] = _updatesWorkingDir;

        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Playwright test server.");
        _server.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (_serverOutput)
                {
                    _serverOutput.AppendLine(e.Data);
                }
            }
        };
        _server.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (_serverError)
                {
                    _serverError.AppendLine(e.Data);
                }
            }
        };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
    }

    private async Task WaitForServerAsync()
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_server is { HasExited: true })
            {
                throw new InvalidOperationException($"The Playwright test server exited early: {GetServerLogs()}");
            }

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/login");
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.RedirectKeepVerb)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"The Playwright test server did not become ready at {BaseUrl}. Logs: {GetServerLogs()}", lastError);
    }

    private string GetServerLogs()
    {
        lock (_serverOutput)
            lock (_serverError)
            {
                return string.Join(Environment.NewLine, new[]
                {
                _serverOutput.ToString(),
                _serverError.ToString()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
    }

    private static string GetArtifactDirectory()
    {
        var path = Path.Combine(GetRepoRoot(), "TestResults", "E2E", "artifacts");
        Directory.CreateDirectory(path);
        return path;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 4; i++)
        {
            dir = dir.Parent ?? throw new InvalidOperationException("Unable to resolve repository root.");
        }

        return dir.FullName;
    }

    private static string ResolveWebDllPath()
    {
        var root = GetRepoRoot();
        var candidates = new[]
        {
            Path.Combine(root, "FinanceManager.Web", "bin", "Debug", "net10.0", "FinanceManager.Web.dll"),
            Path.Combine(root, "FinanceManager.Web", "bin", "Release", "net10.0", "FinanceManager.Web.dll"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate the built FinanceManager.Web.dll.");
    }

    private static void PrepareUpdateSource(string sourceDirectory)
    {
        Directory.CreateDirectory(sourceDirectory);
        var packageBytes = "financemanager-e2e-update-package"u8.ToArray();
        var packagePath = Path.Combine(sourceDirectory, "app.zip");
        using (var stream = File.Create(packagePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("app.txt");
            using var entryStream = entry.Open();
            entryStream.Write(packageBytes);
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        var (platform, runtimeIdentifier) = OperatingSystem.IsWindows() ? ("windows", "win-x64") : ("linux", "linux-x64");
        var manifest = new
        {
            version = AvailableUpdateVersion,
            releaseNotes = "End-to-end test release.",
            publishedAt = DateTimeOffset.UtcNow,
            packages = new[]
            {
                new
                {
                    version = AvailableUpdateVersion,
                    platform,
                    runtimeIdentifier,
                    fileName = "app.zip",
                    uri = new Uri(packagePath).ToString(),
                    sha256,
                    sizeBytes = new FileInfo(packagePath).Length
                }
            }
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(Path.Combine(sourceDirectory, "update.json"), JsonSerializer.Serialize(manifest, options));
    }

    /// <summary>
    /// Seeds <c>release-metadata.json</c> next to the server's content root so <c>ReleaseMetadataInstalledVersionProvider</c>
    /// picks it up. This cannot be redirected to a temporary directory: the server process is started with
    /// <see cref="StartServer"/>'s <c>WorkingDirectory</c> set to the source <c>FinanceManager.Web</c> folder (its
    /// <c>ContentRootPath</c>), which the help feature also depends on (<c>HelpDocumentPathResolver</c> reads
    /// <c>ContentRootPath/../Docs/help</c>) - redirecting the content root away from the source tree would break
    /// that feature for these tests. The original content is captured and restored in
    /// <see cref="RestoreInstalledReleaseMetadata"/>, including via an <see cref="AppDomain.ProcessExit"/> handler
    /// so an aborted test run does not leave the seeded content behind; the file is also excluded via
    /// <c>.gitignore</c> as defense in depth.
    /// </summary>
    private void PrepareInstalledReleaseMetadata()
    {
        _releaseMetadataPath = Path.Combine(GetRepoRoot(), "FinanceManager.Web", "release-metadata.json");
        _originalReleaseMetadata = File.Exists(_releaseMetadataPath) ? File.ReadAllText(_releaseMetadataPath) : null;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var metadata = new
        {
            version = InstalledUpdateVersion,
            publishedAt = DateTimeOffset.UtcNow.AddDays(-30),
            commitSha = (string?)null,
            repository = "FinanceManager",
            runtimeIdentifier = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64"
        };
        File.WriteAllText(_releaseMetadataPath, JsonSerializer.Serialize(metadata, options));
    }

    private void RestoreInstalledReleaseMetadata()
    {
        if (string.IsNullOrWhiteSpace(_releaseMetadataPath))
        {
            return;
        }

        try
        {
            if (_originalReleaseMetadata is null)
            {
                File.Delete(_releaseMetadataPath);
            }
            else
            {
                File.WriteAllText(_releaseMetadataPath, _originalReleaseMetadata);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void DeleteDirectoryBestEffort(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
