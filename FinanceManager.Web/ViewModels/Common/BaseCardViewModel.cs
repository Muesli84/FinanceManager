using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Common;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Components;

namespace FinanceManager.Web.ViewModels.Common
{
    /// <summary>
    /// Base class for card view models. Provides common functionality such as pending field management,
    /// symbol upload handling and optional embedded list view models.
    /// </summary>
    /// <typeparam name="TKeyValue">Type used for key/value pairs in derived card implementations.</typeparam>
    public abstract class BaseCardViewModel<TKeyValue> : BaseViewModel, ISymbolAssignableCard, ICardInitializable
    {
        private string? _initPrefill;
        private string? _initBack;

        /// <summary>
        /// Sets an optional initial prefill payload used when the card is first created/initialized.
        /// </summary>
        /// <param name="prefill">Arbitrary prefill string provided by the caller or navigation context.</param>
        public void SetInitValue(string? prefill) => _initPrefill = prefill;

        /// <summary>
        /// Sets an optional back navigation URL used by the UI to navigate back from the card.
        /// </summary>
        /// <param name="backUrl">Relative or absolute URL to navigate back to, or <c>null</c> to clear.</param>
        public void SetBackNavigation(string? backUrl) => _initBack = backUrl;

        /// <summary>
        /// Default initialize implementation which delegates to <see cref="LoadAsync(Guid)"/>.
        /// Derived implementations may override for custom initialization logic.
        /// </summary>
        /// <param name="id">Identifier used to load the card data.</param>
        /// <returns>A task that completes when initialization has finished.</returns>
        public virtual Task InitializeAsync(System.Guid id) => LoadAsync(id);

        /// <summary>
        /// Optional embedded list view model that can be rendered together with the card (e.g. entries for a statement draft).
        /// When set, the UI may initialize and display the list below the card details.
        /// </summary>
        public BaseListViewModel? EmbeddedList { get; set; }

        /// <summary>
        /// Optional initial prefill string (available to derived classes).
        /// </summary>
        protected string? InitPrefill => _initPrefill;

        /// <summary>
        /// Optional back navigation URL (available to derived classes).
        /// </summary>
        protected string? InitBack => _initBack;

        /// <summary>
        /// Loads the card for the given identifier. Derived classes must implement loading and set the CardRecord / Loading / Error state accordingly.
        /// </summary>
        /// <param name="id">Identifier of the entity to load.</param>
        /// <returns>A task that completes when loading has finished.</returns>
        public abstract Task LoadAsync(System.Guid id);

        /// <summary>
        /// Saves the card. Default implementation is a no-op that returns <c>true</c>.
        /// Derived classes should override to persist pending changes.
        /// </summary>
        /// <returns>A task that resolves to <c>true</c> when save succeeded; otherwise <c>false</c>.</returns>
        public virtual Task<bool> SaveAsync() => Task.FromResult(true);

        /// <summary>
        /// Deletes the underlying entity represented by the card. Default implementation returns <c>false</c>.
        /// Derived classes may override to implement deletion behavior.
        /// </summary>
        /// <returns>A task that resolves to <c>true</c> when deletion succeeded; otherwise <c>false</c>.</returns>
        public virtual Task<bool> DeleteAsync() => Task.FromResult(false);

        /// <summary>
        /// The single card record rendered by the card view. Derived classes should populate this after loading.
        /// </summary>
        public virtual CardRecord? CardRecord { get; protected set; }

        /// <summary>
        /// Pending field values stored by label key. These values are kept in-memory until saved and applied on top of the authoritative CardRecord.
        /// </summary>
        /// <returns>The result.</returns>
        protected readonly Dictionary<string, object?> _pendingFieldValues = new();

        /// <summary>
        /// Initializes a new instance of <see cref="BaseCardViewModel{TKeyValue}"/>.
        /// </summary>
        /// <param name="serviceProvider">Service provider used to resolve dependencies.</param>
        protected BaseCardViewModel(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        /// <summary>
        /// Read-only access to the pending field values dictionary.
        /// </summary>
        public IReadOnlyDictionary<string, object?> PendingFieldValues => _pendingFieldValues;

        /// <summary>
        /// Indicates whether there are unsaved pending changes in the card.
        /// </summary>
        public bool HasPendingChanges => _pendingFieldValues.Count > 0;

        /// <summary>
        /// Optional chart view model displayed alongside the card. Default is <c>null</c>.
        /// </summary>
        public virtual AggregateBarChartViewModel? ChartViewModel => null;

        /// <summary>
        /// Stores a pending value for a simple field. The view model will raise state changed so the UI can update.
        /// </summary>
        /// <param name="field">The card field being edited.</param>
        /// <param name="newValue">The new (pending) value which may be <c>null</c>.</param>
        public virtual void ValidateFieldValue(CardField field, object? newValue)
        {
            if (field == null) return;
            _pendingFieldValues[field.LabelKey] = newValue;
            RaiseStateChanged();
        }

        /// <summary>
        /// Stores a pending lookup field value (selected lookup item) or clears it when <paramref name="item"/> is <c>null</c>.
        /// </summary>
        /// <param name="field">The card field representing the lookup.</param>
        /// <param name="item">Selected lookup item or <c>null</c> to clear.</param>
        public virtual void ValidateLookupField(CardField field, LookupItem? item)
        {
            if (field == null) return;
            if (item == null)
            {
                _pendingFieldValues.Remove(field.LabelKey);
            }
            else
            {
                _pendingFieldValues[field.LabelKey] = item;
            }
            RaiseStateChanged();
        }

        /// <summary>
        /// Clears all pending changes stored in the view model.
        /// </summary>
        protected void ClearPendingChanges() => _pendingFieldValues.Clear();

        /// <summary>
        /// Applies in-memory pending field overrides to the supplied <see cref="CardRecord"/> instance.
        /// This mutates the provided record so the UI can render the pending values without persisting them.
        /// </summary>
        /// <param name="record">The authoritative card record to apply pending values to.</param>
        /// <returns>The same <paramref name="record"/> instance with pending overrides applied.</returns>
        public virtual CardRecord ApplyPendingValues(CardRecord record)
        {
            if (record == null) return record!;
            foreach (var f in record.Fields)
            {
                if (f == null) continue;
                if (!_pendingFieldValues.TryGetValue(f.LabelKey, out var v)) continue;
                // if pending value is a LookupItem, use its name and key
                if (v is LookupItem li)
                {
                    f.ValueId = li.Key;
                    f.Text = li.Name;
                    continue;
                }
                switch (v)
                {
                    case Guid g:
                        f.ValueId = g;
                        if (f.Kind == CardFieldKind.Symbol) f.SymbolId = g;
                        break;
                    case decimal d:
                        f.Amount = d;
                        break;
                    case string s:
                        // try parse decimal for currency fields
                        if (f.Kind == CardFieldKind.Currency && decimal.TryParse(s, out var dv))
                        {
                            f.Amount = dv;
                        }
                        else if (Guid.TryParse(s, out var gv))
                        {
                            f.ValueId = gv;
                            if (f.Kind == CardFieldKind.Symbol) f.SymbolId = gv;
                        }
                        else
                        {
                            f.Text = s;
                        }
                        break;
                    default:
                        f.Text = v?.ToString();
                        break;
                }
            }
            return record;
        }

        // --- Symbol upload handling (default implementation provided in base) ---
        /// <summary>
        /// Determine whether a symbol upload is permitted. Derived classes may override and return <c>false</c>
        /// to disallow uploads or throw exceptions to indicate preconditions are not met.
        /// </summary>
        /// <returns><c>true</c> when upload is permitted; otherwise <c>false</c>.</returns>
        protected abstract bool IsSymbolUploadAllowed();

        /// <summary>
        /// Provides the Attachment parent kind and id to be used for uploading the symbol file.
        /// Implementations should return the appropriate <see cref="AttachmentEntityKind"/> and the (possibly <see cref="Guid.Empty"/>) parent id.
        /// </summary>
        /// <param name="Kind">The kind.</param>
        /// <returns>A tuple of (AttachmentEntityKind, ParentId).</returns>
        protected abstract (AttachmentEntityKind Kind, Guid ParentId) GetSymbolParent();

        /// <summary>
        /// Called after a successful upload so the derived ViewModel can persist the new symbol attachment id
        /// (for example by calling the API to set the symbol on the entity) and update its Model/state.
        /// </summary>
        /// <param name="attachmentId">Attachment id to assign, or <c>null</c> when the symbol was cleared.</param>
        /// <returns>A task that completes when assignment and any necessary state updates are finished.</returns>
        protected abstract Task AssignNewSymbolAsync(Guid? attachmentId);

        /// <summary>
        /// Validate and upload a symbol file for this card's entity. Base implementation performs permission
        /// check, uploads the file as a Symbol role attachment and then calls <see cref="AssignNewSymbolAsync(Guid?)"/>.
        /// </summary>
        /// <param name="stream">Stream containing the file content.</param>
        /// <param name="fileName">Original file name of the symbol.</param>
        /// <param name="contentType">MIME content type of the uploaded file.</param>
        /// <returns>
        /// The created attachment id when upload and assignment succeeded; otherwise <c>null</c>.
        /// </returns>
        /// <remarks>
        /// The base implementation swallows exceptions to preserve existing behavior; derived implementations may log or rethrow as appropriate.
        /// </remarks>
        public virtual async Task<Guid?> ValidateSymbolAsync(System.IO.Stream stream, string fileName, string contentType)
        {
            try
            {
                // Allow derived classes to veto or raise an error
                var allowed = IsSymbolUploadAllowed();
                if (!allowed) return null;

                // Upload attachment using shared API client
                var api = ServiceProvider.GetRequiredService<FinanceManager.Shared.IApiClient>();
                var parent = GetSymbolParent();
                var dto = await api.Attachments_UploadFileAsync((short)parent.Kind, parent.ParentId, stream, fileName, contentType, null, (short)FinanceManager.Domain.Attachments.AttachmentRole.Symbol);
                if (dto != null)
                {
                    await AssignNewSymbolAsync(dto.Id);
                    return dto.Id;
                }
                return null;
            }
            catch
            {
                // Fail silently to preserve previous behavior; derived implementations may log if desired.
                return null;
            }
        }

        /// <summary>
        /// Reload the card data after external changes. Default implementation is a no-op; derived classes may override to re-initialize.
        /// </summary>
        /// <returns>A task that completes when reload has finished.</returns>
        public virtual Task ReloadAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tries to read a <see cref="ParentLinkRequest"/> from the current navigation URL query string.
        /// Expected query parameters are: "parentKind", "parentId" and optional "parentField".
        /// </summary>
        /// <returns>The parsed <see cref="ParentLinkRequest"/> or <c>null</c> when no valid parent context is present.</returns>
        protected ParentLinkRequest? TryGetParentLinkFromQuery()
        {
            try
            {
                var nav = ServiceProvider.GetRequiredService<NavigationManager>();
                var uri = nav.ToAbsoluteUri(nav.Uri);
                var q = QueryHelpers.ParseQuery(uri.Query);

                var pk = q.TryGetValue("parentKind", out var v1) ? v1.ToString() : null;
                var pf = q.TryGetValue("parentField", out var v2) ? v2.ToString() : null;

                Guid pid = Guid.Empty;
                if (q.TryGetValue("parentId", out var v3))
                {
                    _ = Guid.TryParse(v3.ToString(), out pid);
                }

                if (string.IsNullOrWhiteSpace(pk) || pid == Guid.Empty)
                {
                    return null;
                }

                return new ParentLinkRequest(pk!, pid, string.IsNullOrWhiteSpace(pf) ? null : pf);
            }
            catch
            {
                return null;
            }
        }

    }
}
