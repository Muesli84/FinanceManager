namespace FinanceManager.Tests.E2E;

/// <summary>
/// Wraps a single Playwright <see cref="IBrowserContext"/>/<see cref="IPage"/> pair used to drive one E2E
/// test session, and captures diagnostic artifacts (a screenshot, the final page HTML, any browser
/// console/page errors, and an optional trace) on disposal, so a failing test leaves behind enough evidence
/// to diagnose it without needing to rerun it interactively.
/// </summary>
public sealed class PlaywrightBrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly string? _artifactPrefix;
    private readonly bool _artifactCaptureEnabled;
    private readonly bool _traceEnabled;
    private readonly Func<ValueTask>? _onDispose;
    private readonly List<string> _browserMessages = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaywrightBrowserSession"/> class.
    /// </summary>
    /// <param name="context">The browser context that owns <paramref name="page"/> and any trace recording.</param>
    /// <param name="page">The page the test will interact with, exposed via <see cref="Page"/>.</param>
    /// <param name="artifactPrefix">
    /// File path prefix (without extension) used when writing captured artifacts, or <see langword="null"/>
    /// if no artifact directory is available, in which case artifact and trace capture are skipped even if
    /// otherwise enabled.
    /// </param>
    /// <param name="artifactCaptureEnabled">Whether to capture a screenshot, HTML snapshot, and browser console/error log on disposal.</param>
    /// <param name="traceEnabled">Whether to stop and save a Playwright trace on disposal.</param>
    /// <param name="onDispose">Optional cleanup callback invoked when the session is disposed.</param>
    public PlaywrightBrowserSession(IBrowserContext context, IPage page, string? artifactPrefix, bool artifactCaptureEnabled, bool traceEnabled, Func<ValueTask>? onDispose = null)
    {
        _context = context;
        _artifactPrefix = artifactPrefix;
        _artifactCaptureEnabled = artifactCaptureEnabled;
        _traceEnabled = traceEnabled;
        _onDispose = onDispose;
        Page = page;

        if (_artifactCaptureEnabled)
        {
            Page.Console += (_, message) => _browserMessages.Add($"console.{message.Type}: {message.Text}");
            Page.PageError += (_, error) => _browserMessages.Add($"pageerror: {error}");
        }
    }

    /// <summary>
    /// The Playwright page this session drives.
    /// </summary>
    public IPage Page { get; }

    /// <summary>
    /// Captures any enabled diagnostic artifacts (screenshot, HTML snapshot, browser log, trace) for the
    /// session, then disposes the underlying browser context. Artifact capture failures are swallowed so
    /// they never mask the actual test failure they were meant to help diagnose.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_artifactCaptureEnabled && _artifactPrefix != null)
            {
                await Page.ScreenshotAsync(new()
                {
                    Path = $"{_artifactPrefix}.png",
                    FullPage = true
                });

                var html = await Page.ContentAsync();
                await File.WriteAllTextAsync($"{_artifactPrefix}.html", html);

                if (_browserMessages.Count > 0)
                {
                    await File.WriteAllLinesAsync($"{_artifactPrefix}.browser.log", _browserMessages);
                }
            }

            if (_traceEnabled && _artifactPrefix != null)
            {
                await _context.Tracing.StopAsync(new() { Path = $"{_artifactPrefix}.zip" });
            }
        }
        catch
        {
            // Artifact capture must not hide the actual test failure.
        }

        try
        {
            await _context.DisposeAsync();
        }
        finally
        {
            if (_onDispose is not null)
            {
                await _onDispose();
            }
        }
    }
}
