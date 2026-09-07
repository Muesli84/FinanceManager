using Bunit;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Web;
using FinanceManager.Web.Components.Shared;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Component tests for <see cref="ConfirmationDialogHost"/> verifying that it renders
/// the shared <see cref="ConfirmDialog"/> when the confirmation service raises a request
/// and removes it again when the request is completed.
/// </summary>
public sealed class ConfirmationDialogHostTests : BunitContext
{
    /// <summary>
    /// When <see cref="IConfirmationService.ConfirmAsync"/> is called, the host should render
    /// the dialog using the supplied request. Completing the request should remove the dialog.
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_RendersAndDismisseDialog()
    {
        var apiMock = new Mock<FinanceManager.Shared.IApiClient>();
        apiMock.Setup(x => x.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfileSettingsDto { ShowConfirmations = true });

        Services.AddScoped<FinanceManager.Shared.IApiClient>(_ => apiMock.Object);
        Services.AddSingleton<ILogger<ConfirmationService>>(NullLogger<ConfirmationService>.Instance);
        Services.AddSingleton<IStringLocalizer<Pages>>(new PassthroughLocalizer<Pages>());
        Services.AddScoped<IConfirmationService, ConfirmationService>();

        var cut = Render<ConfirmationDialogHost>();
        var service = Services.GetRequiredService<IConfirmationService>();

        var confirmTask = service.ConfirmAsync(new(
            "Confirmation_Delete_Title",
            "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForState(() => cut.Find(".confirm-dialog") != null, timeout: TimeSpan.FromSeconds(5));
        Assert.Contains("Confirmation_Delete_Title", cut.Markup);
        Assert.Contains("Confirmation_Delete_Message", cut.Markup);

        service.SetResult(true);
        await confirmTask;

        cut.WaitForState(() => !cut.Markup.Contains("confirm-dialog"), timeout: TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("confirm-dialog", cut.Markup);
    }

    /// <summary>
    /// Localizer implementation that returns the resource key itself as the value.
    /// </summary>
    /// <typeparam name="T">The type used to scope the localizer.</typeparam>
    private sealed class PassthroughLocalizer<T> : IStringLocalizer<T>
    {
        /// <inheritdoc />
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(name, arguments), resourceNotFound: false);

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();

        /// <inheritdoc />
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
