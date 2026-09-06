using FinanceManager.Application;
using Microsoft.Extensions.Localization;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.Components.Shared;

namespace FinanceManager.Web.ViewModels;

/// <summary>
/// Provides ribbon-related capabilities for view models. Implementations can supply ribbon register definitions
/// and manage an active tab identifier of an arbitrary enum type.
/// </summary>
public interface IRibbonProvider
{
    /// <summary>
    /// Returns ribbon register definitions to be displayed by the UI.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels for the returned registers.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are available.</returns>
    IReadOnlyList<UiRibbonRegister>? GetRibbonRegisters(IStringLocalizer localizer);

    /// <summary>
    /// Retrieves the currently active ribbon tab identifier when available.
    /// </summary>
    /// <typeparam name="TTabEnum">Enumeration type representing ribbon tabs.</typeparam>
    /// <returns>The active tab identifier or <c>null</c> when none is set.</returns>
    TTabEnum? GetActiveTab<TTabEnum>();

    /// <summary>
    /// Sets the currently active ribbon tab identifier.
    /// </summary>
    /// <typeparam name="TTabEnum">Enumeration type representing ribbon tabs.</typeparam>
    /// <param name="id">Tab identifier to set active.</param>
    void SetActiveTab<TTabEnum>(TTabEnum id);
}

/// <summary>
/// Base class for view models used in the Blazor UI. Provides common services resolution, state events,
/// child view model lifecycle management, ribbon aggregation and helper methods for UI actions.
/// </summary>
public abstract class ViewModelBase : IAsyncDisposable, IRibbonProvider
{
    private readonly IServiceProvider _services;
    private readonly List<IAsyncDisposable> _children = new();
    private readonly List<ViewModelBase> _childViewModels = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ICurrentUserService _currentUser;

    /// <summary>
    /// Initializes a new instance of <see cref="ViewModelBase"/> using the provided service provider.
    /// </summary>
    /// <param name="services">Service provider used to resolve application and framework services.</param>
    protected ViewModelBase(IServiceProvider services)
    {
        _services = services;
        _currentUser = services.GetRequiredService<ICurrentUserService>();
    }

    /// <summary>
    /// Exposes the underlying <see cref="IServiceProvider"/> for derived classes to resolve additional services.
    /// </summary>
    protected IServiceProvider Services => _services;

    /// <summary>
    /// Backwards-compatible alias used by existing view models to access the service provider.
    /// </summary>
    protected IServiceProvider ServiceProvider => _services;

    /// <summary>
    /// Event raised when the view model requests the UI to refresh its rendering of bound state.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised when a view model requires the UI to request authentication from the user.
    /// The argument may contain an optional return URL or reason.
    /// </summary>
    public event EventHandler<string?>? AuthenticationRequired;

    /// <summary>
    /// Legacy event raised when the view model requests a simple UI action identified by a string.
    /// </summary>
    public event EventHandler<string?>? UiActionRequested;

    /// <summary>
    /// Event arguments used when a view model requests a UI action. Includes either a string payload or an arbitrary object payload.
    /// </summary>
    public sealed class UiActionEventArgs : EventArgs
    {
        /// <summary>
        /// Action identifier requested by the view model (for example "Back", "OpenAttachments").
        /// </summary>
        public string? Action { get; }

        /// <summary>
        /// Optional string payload associated with the action (legacy support).
        /// </summary>
        public string? Payload { get; }

        /// <summary>
        /// Optional object payload allowing rich payloads such as <see cref="UiOverlaySpec"/>.
        /// </summary>
        public object? PayloadObject { get; }

        /// <summary>
        /// Constructs a UiActionEventArgs with a string payload.
        /// </summary>
        /// <param name="action">Action identifier.</param>
        /// <param name="payload">String payload.</param>
        public UiActionEventArgs(string? action, string? payload)
        {
            Action = action; Payload = payload; PayloadObject = null;
        }

        /// <summary>
        /// Constructs a UiActionEventArgs with an object payload.
        /// </summary>
        /// <param name="action">Action identifier.</param>
        /// <param name="payloadObject">Object payload.</param>
        public UiActionEventArgs(string? action, object? payloadObject)
        {
            Action = action; Payload = null; PayloadObject = payloadObject;
        }
    }

    /// <summary>
    /// Specification for a UI overlay that can be rendered by the page (component type, parameters and modality).
    /// </summary>
    /// <param name="ComponentType">Component type to render.</param>
    /// <param name="Parameters">Optional parameter dictionary passed to the component.</param>
    /// <param name="Modal">If true the overlay is modal.</param>
    /// <returns>The result.</returns>
    public sealed record UiOverlaySpec(Type ComponentType, IReadOnlyDictionary<string, object?>? Parameters = null, bool Modal = true);

    /// <summary>
    /// Rich UI action event carrying an optional object payload.
    /// </summary>
    public event EventHandler<UiActionEventArgs?>? UiActionRequestedEx;

    /// <summary>
    /// Raises <see cref="StateChanged"/> so consumers update the UI state.
    /// </summary>
    protected void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Requests the UI to prompt the user for authentication. An optional return URL may be supplied.
    /// </summary>
    /// <param name="returnUrl">Optional return URL or reason to supply to the authentication flow.</param>
    protected void RequireAuthentication(string? returnUrl = null) => AuthenticationRequired?.Invoke(this, returnUrl);

    /// <summary>
    /// Raises both legacy and rich UI action events with no payload.
    /// </summary>
    /// <param name="action">Action identifier.</param>
    protected void RaiseUiActionRequested(string? action)
    {
        UiActionRequested?.Invoke(this, action);
        UiActionRequestedEx?.Invoke(this, new UiActionEventArgs(action, (string?)null));
    }

    /// <summary>
    /// Raises both legacy and rich UI action events with a string payload.
    /// </summary>
    /// <param name="action">Action identifier.</param>
    /// <param name="payload">String payload to pass to UI consumers.</param>
    protected void RaiseUiActionRequested(string? action, string? payload)
    {
        UiActionRequested?.Invoke(this, action);
        UiActionRequestedEx?.Invoke(this, new UiActionEventArgs(action, payload));
    }

    /// <summary>
    /// Raises both legacy and rich UI action events with an arbitrary object payload.
    /// </summary>
    /// <param name="action">Action identifier.</param>
    /// <param name="payloadObject">Object payload passed to the UI subscriber.</param>
    protected void RaiseUiActionRequested(string? action, object? payloadObject)
    {
        UiActionRequested?.Invoke(this, action);
        UiActionRequestedEx?.Invoke(this, new UiActionEventArgs(action, payloadObject));
    }

    /// <summary>
    /// Convenience helper to request the Attachments overlay from any ViewModel. The overlay spec is raised as a UI action payload.
    /// </summary>
    /// <param name="parentKind">Kind of attachment parent entity.</param>
    /// <param name="parentId">Parent entity id to list attachments for.</param>
    protected void RequestOpenAttachments(AttachmentEntityKind parentKind, Guid parentId)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["ParentKind"] = parentKind,
            ["ParentId"] = parentId
        };
        var spec = new UiOverlaySpec(typeof(AttachmentsPanel), parameters);
        UiActionRequestedEx?.Invoke(this, new UiActionEventArgs("OpenAttachments", spec));
    }

    /// <summary>
    /// Returns true when a current user service is available and the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _currentUser.IsAuthenticated;

    /// <summary>
    /// Creates a child view model instance of type <typeparamref name="T"/>. The created view model is wired to bubble
    /// state and UI events to the parent and will be disposed when the parent is disposed.
    /// </summary>
    /// <typeparam name="T">Type of the child view model to create.</typeparam>
    /// <param name="configure">Optional configuration action invoked after creation.</param>
    /// <returns>The created child view model instance.</returns>
    protected T CreateSubViewModel<T>(Action<T>? configure = null) where T : ViewModelBase
    {
        var vm = ActivatorUtilities.CreateInstance<T>(_services);
        vm.StateChanged += (_, __) => RaiseStateChanged();
        vm.AuthenticationRequired += (_, ret) => AuthenticationRequired?.Invoke(this, ret);
        vm.UiActionRequested += (_, act) => UiActionRequested?.Invoke(this, act);
        vm.UiActionRequestedEx += (_, evt) => UiActionRequestedEx?.Invoke(this, evt);
        _children.Add(vm);
        _childViewModels.Add(vm);
        configure?.Invoke(vm);
        return vm;
    }

    /// <summary>
    /// Cancellation token that is cancelled when the view model is disposed. Use this token for background operations started by the view model.
    /// </summary>
    protected CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// Execution helper that runs the provided action and optionally raises a state change after completion.
    /// </summary>
    /// <param name="action">Function that receives a cancellation token and performs an asynchronous operation.</param>
    /// <param name="raiseAfter">When true <see cref="RaiseStateChanged"/> is invoked after the action completes.</param>
    /// <returns>A task that completes when the action and optional state update have finished.</returns>
    protected async Task RunAsync(Func<CancellationToken, Task> action, bool raiseAfter = true)
    {
        await action(_cts.Token);
        if (raiseAfter) { RaiseStateChanged(); }
    }

    /// <summary>
    /// Optional initialization entry point called by consumers. Derived classes can override to perform async initialization.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel initialization.</param>
    /// <returns>A ValueTask that completes when initialization has finished.</returns>
    public virtual ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <summary>
    /// Aggregates ribbon registers from this view model and its child view models.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels.</param>
    /// <returns>A combined list of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are available.</returns>
    public virtual IReadOnlyList<UiRibbonRegister>? GetRibbonRegisters(IStringLocalizer localizer)
    {
        return GetRibbonRegisterDefinition(localizer);
    }

    /// <summary>
    /// Allows derived classes to provide local ribbon register definitions and aggregates child registers by default.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels.</param>
    /// <returns>Ribbon register definitions or <c>null</c>.</returns>
    protected virtual IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var registers = new List<UiRibbonRegister>();


        // Aggregate registers from child viewmodels
        foreach (var vm in _childViewModels)
        {
            if (vm is IRibbonProvider rp)
            {
                var child = rp.GetRibbonRegisters(localizer);
                if (child != null) registers.AddRange(child);
            }
        }

        return registers.Count == 0 ? null : registers;
    }

    /// <summary>
    /// Compatibility shim: legacy callers expect <c>GetRibbon</c> which delegates to <see cref="GetRibbonRegisterDefinition(IStringLocalizer)"/>
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels.</param>
    /// <returns>Ribbon registers or <c>null</c>.</returns>
    public IReadOnlyList<UiRibbonRegister>? GetRibbon(IStringLocalizer localizer) => GetRibbonRegisterDefinition(localizer);

    /// <summary>
    /// Retrieves the currently active ribbon tab identifier. Default implementation returns <c>null</c>.
    /// Derived classes may override to persist and return tab state.
    /// </summary>
    /// <typeparam name="TTabEnum">Enumeration type representing tabs.</typeparam>
    /// <returns>The active tab identifier or <c>null</c> when none is set.</returns>
    public virtual TTabEnum? GetActiveTab<TTabEnum>() => default;

    /// <summary>
    /// Sets the currently active ribbon tab. Default implementation is a no-op; override as needed.
    /// </summary>
    /// <typeparam name="TTabEnum">Enumeration type representing tabs.</typeparam>
    /// <param name="id">Identifier of the tab to set active.</param>
    public virtual void SetActiveTab<TTabEnum>(TTabEnum id) { }

    /// <summary>
    /// Disposes asynchronous resources held by the view model. Derived classes may override to dispose additional resources.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous dispose operation.</returns>
    public virtual async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var c in _children)
        {
            try { await c.DisposeAsync(); } catch { }
        }
        _children.Clear();
        _childViewModels.Clear();
    }
}