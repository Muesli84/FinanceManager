namespace FinanceManager.Tests.E2E;

/// <summary>
/// Configuration for how Playwright E2E tests launch and drive the browser. Values are read from
/// environment variables with sensible local-run defaults, so CI and local runs can tune browser choice,
/// headedness, timeouts, and diagnostic capture without editing test code.
/// </summary>
public sealed class PlaywrightTestOptions
{
    /// <summary>
    /// The Playwright browser channel to launch (e.g. "msedge", "chrome", "chromium"), read from the
    /// <c>PLAYWRIGHT_BROWSER_CHANNEL</c> environment variable and defaulting to "msedge" so tests run
    /// against a real, installed Edge by default rather than Playwright's bundled Chromium.
    /// </summary>
    /// <returns>The result.</returns>
    public string BrowserChannel { get; init; } = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL") ?? "msedge";

    /// <summary>
    /// Whether the browser is launched headless. Defaults to <see langword="true"/> unless the
    /// <c>PLAYWRIGHT_HEADED</c> environment variable is set to "true", which lets a developer watch a test
    /// run in an actual browser window while debugging locally.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool Headless { get; init; } = !string.Equals(Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Default timeout, in seconds, applied to Playwright actions (clicks, fills, waits for locators, etc.)
    /// on contexts and pages created for a test session.
    /// </summary>
    public int ActionTimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Default timeout, in seconds, applied to page navigations on contexts and pages created for a test
    /// session.
    /// </summary>
    public int NavigationTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Whether to record a Playwright trace for each session, controlled via the <c>PLAYWRIGHT_TRACE</c>
    /// environment variable. Disabled by default because tracing adds overhead; enable it when a failing
    /// test needs deeper diagnosis than a screenshot and HTML snapshot provide.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool TraceEnabled { get; init; } = string.Equals(Environment.GetEnvironmentVariable("PLAYWRIGHT_TRACE"), "1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to capture a screenshot, HTML snapshot, and browser console/page-error log for each session,
    /// controlled via the <c>PLAYWRIGHT_ARTIFACTS</c> environment variable.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool ArtifactCaptureEnabled { get; init; } = string.Equals(Environment.GetEnvironmentVariable("PLAYWRIGHT_ARTIFACTS"), "1", StringComparison.OrdinalIgnoreCase);
}
