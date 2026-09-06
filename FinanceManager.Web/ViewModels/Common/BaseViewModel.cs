using FinanceManager.Shared;
using Microsoft.Extensions.Localization;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos.Admin;
using Microsoft.AspNetCore.Components;
using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Web.ViewModels.Common
{
    /// <summary>
    /// Selection helper used in some UI bindings to represent boolean choices.
    /// </summary>
    public enum BooleanSelection
    {
        /// <summary>
        /// Represents a true/positive selection.
        /// </summary>
        True,

        /// <summary>
        /// Represents a false/negative selection.
        /// </summary>
        False
    }

    /// <summary>
    /// Position where an embedded panel should be rendered on the Card page.
    /// </summary>
    public enum EmbeddedPanelPosition
    {
        /// <summary>
        /// Render the embedded panel after the ribbon (action bar) of the card page.
        /// </summary>
        AfterRibbon,

        /// <summary>
        /// Render the embedded panel after the main card content area.
        /// </summary>
        AfterCard
    }

    /// <summary>
    /// Base class for view models used by the UI. Provides common services resolution, state events, lookup helpers,
    /// ribbon aggregation and child view model lifecycle support.
    /// </summary>
    public abstract class BaseViewModel : IAsyncDisposable, IRibbonProvider
    {
        /// <summary>
        /// Creates a new instance of <see cref="BaseViewModel"/> using the provided service provider.
        /// </summary>
        /// <param name="serviceProvider">Service provider used to resolve dependencies such as <see cref="IApiClient"/>, <see cref="NavigationManager"/>, localization services, etc.</param>
        protected BaseViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        private readonly List<IAsyncDisposable> _children = new();
        private readonly List<BaseViewModel> _childViewModels = new();
        private IApiClient? _ApiClient;
        private NavigationManager? _Navigation;

        /// <summary>
        /// Human-readable title for a view. Derived classes may override.
        /// </summary>
        public virtual string Title { get; } = string.Empty;

        /// <summary>
        /// Indicates whether the view model is currently performing a background operation and the UI should show a loading indicator.
        /// </summary>
        public bool Loading { get; protected set; }

        /// <summary>
        /// Last human readable error message produced by operations in this view model.
        /// </summary>
        public string? LastError { get; protected set; }

        /// <summary>
        /// Optional machine-readable error code coming from the API (for example 'Err_Invalid_bankContactId').
        /// </summary>
        public string? LastErrorCode { get; protected set; }

        /// <summary>
        /// Helper to set the current error state. Looks up localized message when LastErrorCode maps to a localization resource.
        /// </summary>
        /// <param name="errorCode">Machine readable error code or <c>null</c>.</param>
        /// <param name="errorMessage">Fallback human readable message or <c>null</c>.</param>
        protected void SetError(string? errorCode, string? errorMessage)
        {
            LastErrorCode = errorCode;
            LastError = errorMessage;
            if (!string.IsNullOrEmpty(LastErrorCode))
            {
                var entry = Localizer?[LastErrorCode];
                if (entry != null && !entry.ResourceNotFound)
                    LastError = entry.Value;
            }
        }

        /// <summary>
        /// Service provider used to obtain scoped or singleton services.
        /// </summary>
        protected IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Lazily resolved API client instance taken from the service provider.
        /// </summary>
        protected IApiClient ApiClient => _ApiClient ??= ServiceProvider.GetRequiredService<IApiClient>();

        /// <summary>
        /// Lazily resolved navigation manager used for composing navigation URLs.
        /// </summary>
        protected NavigationManager Navigation => _Navigation ??= ServiceProvider.GetRequiredService<NavigationManager>();

        // Lazy-resolved localizer. Resolve on first access and swallow resolution errors (e.g. provider disposed).
        private IStringLocalizer<Pages>? _localizerCache;

        /// <summary>
        /// Localizer used to resolve UI strings for pages/labels. May be <c>null</c> if localization services are not available.
        /// </summary>
        protected IStringLocalizer? Localizer
        {
            get
            {
                if (_localizerCache != null) return _localizerCache;
                try
                {
                    _localizerCache = ServiceProvider.GetService<IStringLocalizer<Pages>>();
                }
                catch
                {
                    _localizerCache = null;
                }
                return _localizerCache;
            }
        }

        /// <summary>
        /// Event raised when the view model requests the UI to refresh its rendering of bound state.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Event raised when the view model requires the UI to request authentication from the user.
        /// Payload may contain an optional reason or return URL.
        /// </summary>
        public event EventHandler<string?>? AuthenticationRequired;

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
        /// Generic overlay specification that pages can render using a DynamicComponent. Contains component type, parameters and modality.
        /// </summary>
        /// <param name="ComponentType">Component type to render.</param>
        /// <param name="Parameters">Optional parameter dictionary passed to the component.</param>
        /// <param name="Modal">If true the overlay is modal.</param>
        public sealed record UiOverlaySpec(Type ComponentType, IReadOnlyDictionary<string, object?>? Parameters = null, bool Modal = true);

        /// <summary>
        /// Specification for embedding an inline panel on card pages. Pages can render this spec at the requested position.
        /// </summary>
        /// <param name="ComponentType">Component type that implements the embedded panel.</param>
        /// <param name="Parameters">Optional parameters passed to the embedded panel component.</param>
        /// <param name="Position">Position on the card page where the panel should be rendered.</param>
        /// <param name="Visible">Whether the panel should be initially visible.</param>
        public sealed record EmbeddedPanelSpec(Type ComponentType, IReadOnlyDictionary<string, object?>? Parameters = null, EmbeddedPanelPosition Position = EmbeddedPanelPosition.AfterCard, bool Visible = true);

        /// <summary>
        /// Event raised when the view model requires the UI to perform an action (overlay, navigation, etc.).
        /// Subscribers receive <see cref="UiActionEventArgs"/> describing the requested action.
        /// </summary>
        public event EventHandler<UiActionEventArgs?>? UiActionRequested;

        /// <summary>
        /// Simple DTO representing a lookup item returned by lookup queries.
        /// </summary>
        /// <param name="Key">Identifier of the lookup item.</param>
        /// <param name="Name">Display name of the lookup item.</param>
        public sealed record LookupItem(System.Guid Key, string Name);

        /// <summary>
        /// Raises <see cref="StateChanged"/> so consumers update the UI state.
        /// </summary>
        protected void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Requests a UI action with no payload.
        /// </summary>
        /// <param name="action">Action identifier.</param>
        protected void RaiseUiActionRequested(string? action) => UiActionRequested?.Invoke(this, new UiActionEventArgs(action, null));

        /// <summary>
        /// Requests a UI action with a string payload.
        /// </summary>
        /// <param name="action">Action identifier.</param>
        /// <param name="payload">String payload to pass to the UI.</param>
        protected void RaiseUiActionRequested(string? action, string? payload) => UiActionRequested?.Invoke(this, new UiActionEventArgs(action, payload));

        /// <summary>
        /// Requests a UI action with an arbitrary object payload.
        /// </summary>
        /// <param name="action">Action identifier.</param>
        /// <param name="payloadObject">Object payload passed to the UI subscriber.</param>
        protected void RaiseUiActionRequested(string? action, object? payloadObject) => UiActionRequested?.Invoke(this, new UiActionEventArgs(action, payloadObject));

        /// <summary>
        /// Convenience helper to request an embedded inline panel on the Card page.
        /// View pages will render the supplied <see cref="EmbeddedPanelSpec"/> at the requested position.
        /// </summary>
        /// <param name="spec">Specification describing the embedded panel to show.</param>
        protected void RaiseUiEmbeddedPanelRequested(EmbeddedPanelSpec spec) => UiActionRequested?.Invoke(this, new UiActionEventArgs("EmbeddedPanel", spec));

        /// <summary>
        /// Background task types that a page should show for this ViewModel. Default: none.
        /// </summary>
        public virtual BackgroundTaskType[]? VisibleBackgroundTaskTypes => Array.Empty<BackgroundTaskType>();

        /// <summary>
        /// Convenience helper for requesting the Attachments overlay from any ViewModel.
        /// This raises a UI action containing a <see cref="UiOverlaySpec"/> the page can render.
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
            RaiseUiActionRequested("OpenAttachments", spec);
        }

        #region Child View Models
        private Dictionary<Type, BaseViewModel> _singletonChildViewModels = new();

        /// <summary>
        /// Creates a child view model of type <typeparamref name="T"/>. Optionally returns a singleton instance per type.
        /// </summary>
        /// <typeparam name="T">Type of the child view model to create.</typeparam>
        /// <param name="singletonPerType">When true the same instance is returned for subsequent calls requesting the same type.</param>
        /// <param name="configure">Optional configuration action invoked after creation (or when returning an existing singleton).</param>
        /// <returns>The created or existing child view model instance.</returns>
        protected T CreateSubViewModel<T>(bool singletonPerType = false, Action<T>? configure = null) where T : BaseViewModel
        {
            if (singletonPerType)
            {
                if (_singletonChildViewModels.TryGetValue(typeof(T), out var existing))
                {
                    configure?.Invoke((T)existing);
                    return (T)existing;
                }
            }
            var vm = ActivatorUtilities.CreateInstance<T>(ServiceProvider);
            vm.StateChanged += (_, __) => RaiseStateChanged();
            vm.AuthenticationRequired += (_, ret) => AuthenticationRequired?.Invoke(this, ret);
            vm.UiActionRequested += (_, act) => UiActionRequested?.Invoke(this, act);
            _singletonChildViewModels.Add(typeof(T), vm);
            _children.Add(vm);
            _childViewModels.Add(vm);
            configure?.Invoke(vm);
            return vm;
        }
        #endregion

        #region Lookup Values
        /// <summary>
        /// Queries lookup values for a CardField. Supports enums, contacts, savings plans, securities and bank accounts.
        /// </summary>
        /// <param name="field">Card field describing the lookup type and optional filter.</param>
        /// <param name="q">Search string to filter lookup results.</param>
        /// <param name="skip">Number of items to skip for paging.
        /// </param>
        /// <param name="take">Number of items to take for paging.</param>
        /// <returns>A task that resolves to a list of <see cref="LookupItem"/> matching the query.</returns>
        public virtual async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(CardField field, string? q, int skip, int take)
        {
            if (!string.IsNullOrWhiteSpace(field.LookupType) && field.LookupType.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase))
            {
                var enumName = field.LookupType.Substring("Enum:".Length);
                var enumType = ResolveEnumType(enumName);
                if (enumType != null && enumType.IsEnum)
                    return QueryEnumLookup(q, enumType);
                return Array.Empty<LookupItem>();
            }

            if (string.Equals(field.LookupType, "Contact", StringComparison.OrdinalIgnoreCase))
                return await QueryContactLookupAsync(field, q, skip, take);
            if (string.Equals(field.LookupType, "ContactCategory", StringComparison.OrdinalIgnoreCase))
                return await QueryContactCategoryLookupAsync(field, q, skip, take);

            if (string.Equals(field.LookupType, "SavingsPlan", StringComparison.OrdinalIgnoreCase))
                return await QuerySavingsPlanLookupAsync(field, q, skip, take);

            if (string.Equals(field.LookupType, "Security", StringComparison.OrdinalIgnoreCase))
                return await QuerySecurityLookupAsync(field, q, skip, take);

            // Bank account lookup support
            if (string.Equals(field.LookupType, "bankaccount", StringComparison.OrdinalIgnoreCase) || string.Equals(field.LookupType, "Account", StringComparison.OrdinalIgnoreCase))
                return await QueryAccountLookupAsync(field, q, skip, take);

            if (string.Equals(field.LookupType, "BudgetCategory", StringComparison.OrdinalIgnoreCase))
                return await QueryBudgetCategoryLookupAsync(field, q, skip, take);

            return Array.Empty<LookupItem>();
        }

        private async Task<IReadOnlyList<LookupItem>> QueryContactLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            ContactType? typeFilter = null;
            if (!string.IsNullOrWhiteSpace(field.LookupFilter))
            {
                var parts = field.LookupFilter.Split('=', 2);
                if (parts.Length == 2 && string.Equals(parts[0].Trim(), "Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<ContactType>(parts[1].Trim(), ignoreCase: true, out var ct)) typeFilter = ct;
                }
            }
            var results = await api.Contacts_ListAsync(skip, take, typeFilter, false, q);
            return results.Select(c => new LookupItem(c.Id, c.Name)).ToList();
        }
        private async Task<IReadOnlyList<LookupItem>> QueryContactCategoryLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();

            // Currently the API does not provide server-side filtering/paging for contact categories.
            // Load all and apply filtering/paging client-side.
            var list = await api.ContactCategories_ListAsync(CancellationToken.None);

            IEnumerable<ContactCategoryDto> filtered = list ?? Array.Empty<ContactCategoryDto>();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                filtered = filtered.Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            filtered = filtered
                .Skip(Math.Max(0, skip))
                .Take(take <= 0 ? 50 : take);

            return filtered
                .Select(c => new LookupItem(c.Id, c.Name ?? string.Empty))
                .ToList();
        }

        private bool ParseOnlyActiveFilter(string? filter, bool defaultValue = true)
        {
            if (string.IsNullOrWhiteSpace(filter)) return defaultValue;
            var parts = filter.Split('=', 2);
            if (parts.Length != 2) return defaultValue;
            if (!string.Equals(parts[0].Trim(), "OnlyActive", StringComparison.OrdinalIgnoreCase)) return defaultValue;
            if (bool.TryParse(parts[1].Trim(), out var parsed)) return parsed;
            return defaultValue;
        }

        private async Task<IReadOnlyList<LookupItem>> QuerySavingsPlanLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            // allow optional lookup filter like "OnlyActive=true"
            var onlyActive = ParseOnlyActiveFilter(field.LookupFilter, defaultValue: true);
            var all = await api.SavingsPlans_ListAsync(onlyActive, CancellationToken.None);
            var filtered = all
                .Where(sp => string.IsNullOrWhiteSpace(q) || (sp.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .Skip(skip).Take(take)
                .Select(sp => new LookupItem(sp.Id, sp.Name))
                .ToList();
            return filtered;
        }

        private async Task<IReadOnlyList<LookupItem>> QuerySecurityLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            var onlyActive = ParseOnlyActiveFilter(field.LookupFilter, defaultValue: true);
            var all = await api.Securities_ListAsync(onlyActive, CancellationToken.None);
            var filtered = all
                .Where(sx => string.IsNullOrWhiteSpace(q) || (sx.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) || (sx.Identifier?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .Skip(skip).Take(take)
                .Select(sx => new LookupItem(sx.Id, sx.Name))
                .ToList();
            return filtered;
        }

        private async Task<IReadOnlyList<LookupItem>> QueryAccountLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            Guid? bankContactId = null;
            if (!string.IsNullOrWhiteSpace(field.LookupFilter))
            {
                var parts = field.LookupFilter.Split('=', 2);
                if (parts.Length == 2 && string.Equals(parts[0].Trim(), "BankContactId", StringComparison.OrdinalIgnoreCase))
                {
                    if (Guid.TryParse(parts[1].Trim(), out var parsed)) bankContactId = parsed;
                }
            }

            var results = await api.GetAccountsAsync(skip, take, bankContactId, q: null, CancellationToken.None);
            var filtered = results
                .Where(a => string.IsNullOrWhiteSpace(q) || (a.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) || (a.Iban?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .Select(a => new LookupItem(a.Id, string.IsNullOrWhiteSpace(a.Iban) ? a.Name : $"{a.Name} ({a.Iban})"))
                .ToList();
            return filtered;
        }

        private async Task<IReadOnlyList<LookupItem>> QueryBudgetCategoryLookupAsync(CardField field, string? q, int skip, int take)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();

            var list = await api.Budgets_ListCategoriesAsync(ct: CancellationToken.None);

            IEnumerable<BudgetCategoryOverviewDto> filtered = list;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                filtered = filtered.Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            filtered = filtered
                .Skip(Math.Max(0, skip))
                .Take(take <= 0 ? 50 : take);

            return filtered
                .Select(c => new LookupItem(c.Id, c.Name ?? string.Empty))
                .ToList();
        }

        private IReadOnlyList<LookupItem> QueryEnumLookup(string? q, Type enumType)
        {
            var items = Enum.GetValues(enumType).Cast<object>()
                .Select(v =>
                {
                    var raw = v?.ToString() ?? string.Empty;
                    // Try localized enum label first (key: EnumType_{EnumName}_{Member}) and fall back to raw member name
                    string name = raw;
                    try
                    {
                        var key = $"EnumType_{enumType.Name}_{raw}";
                        var val = Localizer?[key];
                        if (val != null && !val.ResourceNotFound && !string.IsNullOrWhiteSpace(val.Value)) name = val.Value;
                    }
                    catch { }
                    return new LookupItem(Guid.Empty, name);
                })
                .Where(li => string.IsNullOrWhiteSpace(q) || li.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return items;
        }
        #endregion Lookup Values

        /// <summary>
        /// Applies localized translations for enum-based lookup fields on the supplied record.
        /// </summary>
        /// <param name="record">Card record to translate.</param>
        /// <returns>The translated record (same instance mutated).</returns>
        protected virtual CardRecord ApplyEnumTranslations(CardRecord record)
        {
            foreach (var field in record.Fields)
                ApplyEnumTranslation(field);
            return record;
        }

        private void ApplyEnumTranslation(CardField field)
        {
            if (!string.IsNullOrWhiteSpace(field.LookupType) && field.LookupType.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase))
            {
                var enumName = field.LookupType.Substring("Enum:".Length);
                var enumType = ResolveEnumType(enumName);
                if (enumType != null && enumType.IsEnum)
                {
                    var key = $"EnumType_{enumType.Name}_{field.Text}";
                    var val = Localizer?[key];
                    if (val != null && !string.IsNullOrWhiteSpace(val))
                        field.Text = val.Value;
                }
            }
        }

        private Type? ResolveEnumType(string enumName)
        {
            // Try known namespace first
            var candidates = new[] {
                $"FinanceManager.Shared.Dtos.Accounts.{enumName}",
                enumName
            };
            foreach (var n in candidates)
            {
                try
                {
                    var t = Type.GetType(n, throwOnError: false, ignoreCase: true);
                    if (t != null && t.IsEnum) return t;
                }
                catch { }
            }

            // Search loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    var t = asm.GetTypes().FirstOrDefault(x => x.IsEnum && (string.Equals(x.Name, enumName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.FullName, enumName, StringComparison.OrdinalIgnoreCase) || x.FullName?.EndsWith("." + enumName, StringComparison.OrdinalIgnoreCase) == true));
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Determines whether the provided child view model should be considered active for purposes of aggregating its ribbon registers.
        /// Derived classes may override to control which child view models contribute ribbon items.
        /// </summary>
        /// <param name="vm">Child view model to evaluate.</param>
        /// <returns><c>true</c> when the child view model is active; otherwise <c>false</c>.</returns>
        protected virtual bool IsChildViewModelActive(BaseViewModel vm)
        {
            return true;
        }

        // IRibbonProvider implementation
        /// <summary>
        /// Aggregates ribbon registers from this view model and its active child view models.
        /// </summary>
        /// <param name="localizer">Localizer used to resolve UI labels.</param>
        /// <returns>A combined list of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are available.</returns>
        public virtual IReadOnlyList<UiRibbonRegister>? GetRibbonRegisters(IStringLocalizer localizer)
        {
            var regs = GetRibbonRegisterDefinition(localizer);
            var registers = new List<UiRibbonRegister>(regs ?? new UiRibbonRegister[0]);

            // Aggregate registers from child viewmodels
            foreach (var vm in _childViewModels.Where(vm => IsChildViewModelActive(vm)))
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
        /// Allows derived classes to provide local ribbon register definitions. Default implementation returns <c>null</c>.
        /// </summary>
        /// <param name="localizer">Localizer used to resolve labels.</param>
        /// <returns>Ribbon register definitions or <c>null</c>.</returns>
        protected virtual IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer) => null;

        /// <summary>
        /// Compatibility shim: legacy callers expect <c>GetRibbon</c> which delegates to <see cref="GetRibbonRegisterDefinition(IStringLocalizer)"/>
        /// </summary>
        /// <param name="localizer">Localizer used to resolve labels.</param>
        /// <returns>Ribbon registers or <c>null</c>.</returns>
        public IReadOnlyList<UiRibbonRegister>? GetRibbon(IStringLocalizer localizer) => GetRibbonRegisterDefinition(localizer);

        /// <summary>
        /// Sets the currently active ribbon tab. Default implementation is a no-op; override as needed.
        /// </summary>
        /// <typeparam name="TTabEnum">Enumeration type representing tabs.</typeparam>
        /// <param name="id">Identifier of the tab to set active.</param>
        public void SetActiveTab<TTabEnum>(TTabEnum id)
        {
        }

        /// <summary>
        /// Retrieves the currently active ribbon tab identifier. Default implementation returns <c>null</c>.
        /// </summary>
        /// <typeparam name="TTabEnum">Enumeration type representing tabs.</typeparam>
        /// <returns>The active tab identifier or <c>null</c> when none is set.</returns>
        public TTabEnum? GetActiveTab<TTabEnum>()
        {
            return default;
        }


        /// <summary>
        /// Disposes asynchronous resources held by the view model. Derived classes may override to dispose additional resources.
        /// </summary>
        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Returns true when a current user service is available and the user is authenticated.
        /// This provides a simple way for viewmodels and components to check authentication state.
        /// </summary>
        public bool IsAuthenticated
        {
            get
            {
                try
                {
                    var cur = ServiceProvider.GetService<ICurrentUserService>();
                    return cur?.IsAuthenticated ?? false;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks the current authentication state using <see cref="ICurrentUserService"/> and raises
        /// the <see cref="AuthenticationRequired"/> event when the user is not authenticated.
        /// Returns true when the user is authenticated (or no current-user service is available).
        /// </summary>
        /// <returns><c>true</c> when the user is authenticated; otherwise <c>false</c> and the <see cref="AuthenticationRequired"/> event is raised.</returns>
        public bool CheckAuthentication()
        {
            try
            {
                var cur = ServiceProvider.GetService<FinanceManager.Application.ICurrentUserService>();
                if (cur != null && !cur.IsAuthenticated)
                {
                    AuthenticationRequired?.Invoke(this, null);
                    return false;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Allows external UI components (e.g. EmbeddedPanelHost) to notify the view model that an embedded panel finished
        /// and pass an optional payload. This method forwards the notification as a UiActionRequested with action
        /// "EmbeddedFinished" so view models can react to the embedded panel result.
        /// </summary>
        /// <param name="payloadObject">Optional payload provided by the embedded panel.</param>
        public virtual void EmbeddedPanelFinished(object? payloadObject)
        {
            // Reuse existing RaiseUiActionRequested helper to notify subscribers
            RaiseUiActionRequested("EmbeddedFinished", payloadObject);
        }
    }
}
