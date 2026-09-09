using System.Net;
using Bunit;
using FinanceManager.Application;
using FluentAssertions;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Admin;
using FinanceManager.Web.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Tests for <see cref="BackgroundTaskStatusPanel"/>: it must only poll for active background
/// tasks while the current user is actually authenticated (via either the server-side
/// <see cref="ICurrentUserService"/> or the client-side circuit auth fallback), keep polling
/// through transient request failures, and stop polling once the API starts returning
/// Unauthorized - so a signed-out session doesn't keep hammering a protected endpoint.
/// </summary>
public sealed class BackgroundTaskStatusPanelTests : BunitContext
{
    /// <summary>
    /// Verifies that when neither the server-side auth service nor the client-side circuit auth
    /// fallback report the user as authenticated, the panel never calls the active-tasks endpoint
    /// at all, avoiding an unauthenticated request that would just fail.
    /// </summary>
    [Fact]
    public void DoesNotLoadTasks_WhenUserIsNotAuthenticated()
    {
        var apiMock = new Mock<IApiClient>();
        RegisterServices(apiMock, isAuthenticated: false, jsAuthenticated: false);

        Render<BackgroundTaskStatusPanel>();

        apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that even when <see cref="ICurrentUserService"/> reports the user as not
    /// authenticated, the panel still loads and renders tasks if the client-side circuit auth
    /// fallback ("fmAuthIsAuthenticated" JS check) says the user is authenticated - covering the
    /// case where server-side auth state hasn't caught up yet after a Blazor Server reconnect.
    /// </summary>
    [Fact]
    public void LoadsTasks_WhenCircuitAuthFallbackIsAuthenticated()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateTask(BackgroundTaskStatus.Running) });
        RegisterServices(apiMock, isAuthenticated: false, jsAuthenticated: true);

        var cut = Render<BackgroundTaskStatusPanel>(parameters => parameters.Add(p => p.PollInterval, 10_000));

        cut.WaitForAssertion(() =>
        {
            apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotNull(cut.Find(".bgt-panel"));
        });
    }

    /// <summary>
    /// Verifies the straightforward happy path: an authenticated user causes the panel to call the
    /// active-tasks endpoint and render the panel container once tasks are returned.
    /// </summary>
    [Fact]
    public void LoadsAndRendersTasks_WhenUserIsAuthenticated()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateTask(BackgroundTaskStatus.Running) });
        RegisterServices(apiMock, isAuthenticated: true);

        var cut = Render<BackgroundTaskStatusPanel>(parameters => parameters.Add(p => p.PollInterval, 10_000));

        cut.WaitForAssertion(() =>
        {
            apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotNull(cut.Find(".bgt-panel"));
        });
    }

    /// <summary>
    /// Verifies that the panel renders a running <see cref="BackgroundTaskType.CreateDemoData"/>
    /// task with its progress counters and message in the generic active-task UI.
    /// </summary>
    [Fact]
    public void RendersDemoDataTaskProgress_WhenCreateDemoDataTaskIsRunning()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateTask(BackgroundTaskStatus.Running, BackgroundTaskType.CreateDemoData, 2, 5, "Demo-Daten werden angelegt...") });
        RegisterServices(apiMock, isAuthenticated: true);

        var cut = Render<BackgroundTaskStatusPanel>(parameters => parameters.Add(p => p.PollInterval, 10_000));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Bgt_Type_CreateDemoData");
            cut.Markup.Should().Contain("2 / 5");
            cut.Markup.Should().Contain("Demo-Daten werden angelegt...");
        });
    }

    /// <summary>
    /// Verifies that once the active-tasks request fails with an HTTP 401 Unauthorized, the panel
    /// stops scheduling further poll requests entirely (verified by waiting past several poll
    /// intervals and confirming the call count stays at one) - a session that has been signed out
    /// server-side must not keep retrying a call it can never succeed at.
    /// </summary>
    [Fact]
    public void StopsPolling_WhenActiveTasksRequestReturnsUnauthorized()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        RegisterServices(apiMock, isAuthenticated: true);

        var cut = Render<BackgroundTaskStatusPanel>(parameters => parameters.Add(p => p.PollInterval, 10));

        cut.WaitForAssertion(() =>
            apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once));

        Task.Delay(75, Xunit.TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a non-401 failure (e.g. a transient network error) on one poll does not stop
    /// the polling loop: the panel retries on the next interval and successfully renders once the
    /// call starts succeeding again - distinguishing a temporary glitch from the "give up" signal
    /// covered by <see cref="StopsPolling_WhenActiveTasksRequestReturnsUnauthorized"/>.
    /// </summary>
    [Fact]
    public void KeepsPolling_WhenActiveTasksRequestFailsTransiently()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.SetupSequence(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Temporary failure"))
            .ReturnsAsync(new[] { CreateTask(BackgroundTaskStatus.Queued) });
        RegisterServices(apiMock, isAuthenticated: true);

        var cut = Render<BackgroundTaskStatusPanel>(parameters => parameters.Add(p => p.PollInterval, 10));

        cut.WaitForAssertion(() =>
        {
            apiMock.Verify(x => x.BackgroundTasks_GetActiveAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
            Assert.NotNull(cut.Find(".bgt-panel"));
        });
    }

    private void RegisterServices(Mock<IApiClient> apiMock, bool isAuthenticated, bool? jsAuthenticated = null)
    {
        Services.AddSingleton(apiMock.Object);
        Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(PassthroughLocalizer<>));

        if (jsAuthenticated.HasValue)
        {
            JSInterop.Setup<bool>("fmAuthIsAuthenticated").SetResult(jsAuthenticated.Value);
        }
    }

    private static BackgroundTaskInfo CreateTask(
        BackgroundTaskStatus status,
        BackgroundTaskType type = BackgroundTaskType.ClassifyAllDrafts,
        int? processed = 1,
        int? total = 2,
        string? message = "Processing")
        => new(
            Guid.NewGuid(),
            type,
            Guid.NewGuid(),
            DateTime.UtcNow,
            status,
            processed,
            total,
            message,
            0,
            0,
            null,
            status == BackgroundTaskStatus.Running ? DateTime.UtcNow : null,
            null,
            null,
            null,
            null,
            null);

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsAdmin { get; set; }
    }

    private sealed class PassthroughLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments), resourceNotFound: false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
