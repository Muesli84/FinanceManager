using Bunit;
using FinanceManager.Web;
using FinanceManager.Web.Components.Shared;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Component tests for <see cref="ConfirmDialog"/> verifying rendering and callback behavior.
/// </summary>
public sealed class ConfirmDialogTests : BunitContext
{
    /// <summary>
    /// Verifies that the dialog renders the title and message supplied by the confirmation request.
    /// </summary>
    [Fact]
    public void ConfirmDialog_ShouldRenderTitleAndMessage()
    {
        RegisterServices();

        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        var cut = Render<ConfirmDialog>(parameters => parameters.Add(p => p.Request, request));

        Assert.Contains("Confirmation_Delete_Title", cut.Markup);
        Assert.Contains("Confirmation_Delete_Message", cut.Markup);
    }

    /// <summary>
    /// Verifies that clicking the confirm button invokes <see cref="IConfirmationService.SetResult"/> with true.
    /// </summary>
    [Fact]
    public void ConfirmDialog_ShouldCallSetResultTrue_OnConfirm()
    {
        var svcMock = new Mock<IConfirmationService>();
        RegisterServices(svcMock.Object);

        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        var cut = Render<ConfirmDialog>(parameters => parameters.Add(p => p.Request, request));

        var confirmButton = cut.FindAll("button").First(b => b.TextContent.Contains("Btn_Confirm"));
        confirmButton.Click();

        svcMock.Verify(x => x.SetResult(true), Times.Once);
    }

    /// <summary>
    /// Verifies that clicking the cancel button invokes <see cref="IConfirmationService.Cancel"/>.
    /// </summary>
    [Fact]
    public void ConfirmDialog_ShouldCallCancel_OnCancel()
    {
        var svcMock = new Mock<IConfirmationService>();
        RegisterServices(svcMock.Object);

        var request = new ConfirmationRequest("Confirmation_Delete_Title", "Confirmation_Delete_Message");
        var cut = Render<ConfirmDialog>(parameters => parameters.Add(p => p.Request, request));

        var cancelButton = cut.FindAll("button").First(b => b.TextContent.Contains("Btn_Cancel"));
        cancelButton.Click();

        svcMock.Verify(x => x.Cancel(), Times.Once);
    }

    /// <summary>
    /// Registers the services required to render <see cref="ConfirmDialog"/> in a bUnit test context.
    /// </summary>
    /// <param name="confirmationService">Optional confirmation service mock; a default mock is used when null.</param>
    private void RegisterServices(IConfirmationService? confirmationService = null)
    {
        Services.AddSingleton(confirmationService ?? Mock.Of<IConfirmationService>());
        Services.AddSingleton<IStringLocalizer<Pages>>(new PassthroughLocalizer<Pages>());
    }

    /// <summary>
    /// Localizer implementation that returns the resource key itself as the value, simplifying tests
    /// that assert on rendered markup without depending on actual localization files.
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
