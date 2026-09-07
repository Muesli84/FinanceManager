using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Web.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManager.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ConfirmationService"/> verifying the dialog suppression
/// and confirmation flow behavior.
/// </summary>
public sealed class ConfirmationServiceTests
{
    /// <summary>
    /// When the user has disabled confirmation dialogs the service should return true immediately
    /// without raising the <see cref="ConfirmationService.OnShow"/> event.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_ReturnsTrue_WhenShowConfirmationsIsFalse()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = false });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");

        var result = await sut.ConfirmAsync(request);

        Assert.True(result);
        apiMock.Verify(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// When the user has enabled confirmation dialogs the service should raise <see cref="ConfirmationService.OnShow"/>
    /// and wait for the dialog to confirm before returning true.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_RaisesOnShow_AndReturnsTrue_WhenUserConfirms()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        var onShowFired = false;
        sut.OnShow += (_, _) =>
        {
            onShowFired = true;
            sut.SetResult(true);
        };

        var result = await sut.ConfirmAsync(request);

        Assert.True(onShowFired);
        Assert.True(result);
    }

    /// <summary>
    /// When the user has enabled confirmation dialogs and the dialog is cancelled, the service should return false.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_RaisesOnShow_AndReturnsFalse_WhenUserCancels()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        sut.OnShow += (_, _) => sut.Cancel();

        var result = await sut.ConfirmAsync(request);

        Assert.False(result);
    }

    /// <summary>
    /// API failures while fetching the user preference should default to showing the confirmation dialog.
    /// With a handler that confirms the dialog the request still succeeds.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_DefaultsToEnabled_WhenApiThrows()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("network"));

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        sut.OnShow += (_, _) => sut.SetResult(true);

        var result = await sut.ConfirmAsync(request);

        Assert.True(result);
    }

    /// <summary>
    /// The preference value should be cached so subsequent confirmation requests do not repeatedly call the API.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_UsesCachedValue_OnSecondCall()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        sut.OnShow += (_, _) => sut.SetResult(true);

        _ = await sut.ConfirmAsync(request);
        _ = await sut.ConfirmAsync(request);

        apiMock.Verify(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Calling <see cref="ConfirmationService.InvalidateCacheAsync"/> should force the service to re-fetch
    /// the user preference on the next confirmation request.
    /// </summary>
    [Fact]
    public async Task InvalidateCacheAsync_RefetchesProfile_OnNextConfirm()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.SetupSequence(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true })
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = false });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        sut.OnShow += (_, _) => sut.SetResult(true);

        _ = await sut.ConfirmAsync(request);
        await sut.InvalidateCacheAsync();
        var result = await sut.ConfirmAsync(request);

        Assert.True(result);
        apiMock.Verify(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// The service should expose the current confirmation request before it completes, allowing the dialog
    /// host to bind to the latest request.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_SetsCurrentRequest_BeforeReturning()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true });

        var sut = CreateSut(apiMock.Object);
        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        sut.OnShow += (_, r) =>
        {
            Assert.Same(request, r);
            Assert.Same(request, sut.CurrentRequest);
            sut.SetResult(false);
        };

        _ = await sut.ConfirmAsync(request);
    }

    /// <summary>
    /// All confirmation severity values must be defined so UI styling can distinguish default,
    /// warning and destructive confirmations.
    /// </summary>
    /// <param name="severity">The severity value to verify.</param>
    [Theory]
    [InlineData(ConfirmationSeverity.Default)]
    [InlineData(ConfirmationSeverity.Warning)]
    [InlineData(ConfirmationSeverity.Critical)]
    public void SeverityValues_AreDefined(ConfirmationSeverity severity)
    {
        Assert.True(Enum.IsDefined(typeof(ConfirmationSeverity), severity));
    }

    /// <summary>
    /// Creates a <see cref="ConfirmationService"/> instance for testing using the supplied API client.
    /// </summary>
    /// <param name="api">The mocked API client.</param>
    /// <returns>A configured <see cref="ConfirmationService"/>.</returns>
    private static ConfirmationService CreateSut(IApiClient api)
        => new(api, Mock.Of<ILogger<ConfirmationService>>());
}
