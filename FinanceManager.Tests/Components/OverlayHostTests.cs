using Bunit;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared;
using FinanceManager.Web;
using FinanceManager.Web.Components.Shared;
using FinanceManager.Web.ViewModels;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components;

/// <summary>
/// Covers <see cref="OverlayHost{T}"/>, the generic component that renders an arbitrary component
/// (looked up by <c>Type</c> plus a parameter dictionary) inside a modal-style card whenever a
/// view model raises an "OpenOverlay" UI action. These tests guard the header title resolution
/// logic: an explicitly supplied "OverlayTitle" parameter must win, and when it is absent the host
/// must fall back to a localized title based on the overlay's component type.
/// </summary>
public sealed class OverlayHostTests : BunitContext
{
    /// <summary>
    /// Verifies that an explicit overlay title parameter is rendered in the host header.
    /// </summary>
    [Fact]
    public void OverlayHost_ShouldRenderOverlayTitle_WhenProvidedInParameters()
    {
        Services.AddSingleton(Mock.Of<IApiClient>());
        Services.AddSingleton<IStringLocalizer<Pages>>(new PassthroughLocalizer<Pages>());

        var provider = new TestCardViewModel(Services);
        var cut = Render<OverlayHost<(string, string)>>(parameters => parameters
            .Add(p => p.Provider, provider)
            .Add(p => p.Localizer, new PassthroughLocalizer<Pages>()));

        provider.RaiseOverlay(new BaseViewModel.UiOverlaySpec(
            typeof(SecurityPriceImportPanel),
            new Dictionary<string, object?>
            {
                ["OverlayTitle"] = "Import prices custom",
                ["SecurityId"] = Guid.NewGuid()
            }));

        cut.WaitForAssertion(() => Assert.Equal("Import prices custom", cut.Find("h2").TextContent));
    }

    /// <summary>
    /// Verifies that title mapping falls back to localized import title for the import panel.
    /// </summary>
    [Fact]
    public void OverlayHost_ShouldUseLocalizedFallbackTitle_WhenOverlayTitleIsMissing()
    {
        Services.AddSingleton(Mock.Of<IApiClient>());
        Services.AddSingleton<IStringLocalizer<Pages>>(new PassthroughLocalizer<Pages>());

        var provider = new TestCardViewModel(Services);
        var cut = Render<OverlayHost<(string, string)>>(parameters => parameters
            .Add(p => p.Provider, provider)
            .Add(p => p.Localizer, new PassthroughLocalizer<Pages>()));

        provider.RaiseOverlay(new BaseViewModel.UiOverlaySpec(
            typeof(SecurityPriceImportPanel),
            new Dictionary<string, object?>
            {
                ["SecurityId"] = Guid.NewGuid()
            }));

        cut.WaitForAssertion(() => Assert.Equal("SecurityPricesImport_Title", cut.Find("h2").TextContent));
    }

    /// <summary>
    /// Minimal <see cref="BaseCardViewModel{T}"/> stand-in used only to trigger the "OpenOverlay" UI
    /// action that <see cref="OverlayHost{T}"/> listens for. The abstract symbol-upload members are
    /// stubbed with no-ops since they are unrelated to overlay rendering and never exercised here.
    /// </summary>
    private sealed class TestCardViewModel : BaseCardViewModel<(string Key, string Value)>
    {
        public TestCardViewModel(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override Task LoadAsync(Guid id) => Task.CompletedTask;

        protected override bool IsSymbolUploadAllowed() => false;

        protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.Security, Guid.Empty);

        protected override Task AssignNewSymbolAsync(Guid? attachmentId) => Task.CompletedTask;

        /// <summary>
        /// Raises the "OpenOverlay" UI action with the given spec, simulating what a real view model
        /// does when it wants <see cref="OverlayHost{T}"/> to display an overlay component.
        /// </summary>
        /// <param name="spec">The overlay component type and parameters to open.</param>
        public void RaiseOverlay(BaseViewModel.UiOverlaySpec spec)
        {
            RaiseUiActionRequested("OpenOverlay", spec);
        }
    }

    /// <summary>
    /// Fake <see cref="IStringLocalizer{T}"/> that echoes each resource key back as its own value
    /// (or, for known keys such as "SecurityPricesImport_Title", the fixed localized fallback string
    /// asserted by the tests) so localization behavior can be verified without loading real
    /// resource files.
    /// </summary>
    private sealed class PassthroughLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
