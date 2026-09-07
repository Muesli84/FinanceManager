namespace FinanceManager.Web.ViewModels.Setup;

using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// View model for managing attachment categories in the setup UI.
/// Provides state and operations for listing, creating, editing and deleting attachment categories.
/// This view model is intended to be used by Blazor components and follows the UI state conventions
/// used across the project (properties for busy/loading state, error handling via <see cref="BaseViewModel"/>).
/// </summary>
public sealed class SetupAttachmentCategoriesViewModel : BaseViewModel
{

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupAttachmentCategoriesViewModel"/> class.
    /// </summary>
    /// <param name="sp">The service provider used by <see cref="BaseViewModel"/> to resolve services (e.g. API client).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sp"/> is null and required services cannot be resolved.</exception>
    public SetupAttachmentCategoriesViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Gets the list of attachment category items currently loaded into the view model.
    /// The collection is mutable (items are added/removed) but the property itself is read-only.
    /// </summary>
    /// <returns>The result.</returns>
    public List<AttachmentCategoryDto> Items { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the view model is currently performing an operation that prevents user interaction (e.g. saving or deleting).
    /// </summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last action completed successfully.
    /// This can be used by the UI to show a short success indicator.
    /// </summary>
    public bool ActionOk { get; private set; }

    /// <summary>
    /// Gets or sets the name entered for creating a new category.
    /// </summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the current <see cref="NewName"/> is valid for creating a new category.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool CanAdd => !string.IsNullOrWhiteSpace(NewName) && NewName.Trim().Length >= 2;

    /// <summary>
    /// Gets the id of the category currently being edited. <see cref="Guid.Empty"/> when not editing.
    /// </summary>
    public Guid EditId { get; private set; }

    /// <summary>
    /// Gets or sets the name value used for editing an existing category.
    /// </summary>
    public string EditName { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the current <see cref="EditName"/> is valid for saving the edit.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool CanSaveEdit => !string.IsNullOrWhiteSpace(EditName) && EditName.Trim().Length >= 2;

    /// <summary>
    /// Resets UI action state (clears success indicator and error) and notifies the UI that state changed.
    /// Call this from input handlers when the user modifies form fields.
    /// </summary>
    public void OnChanged()
    {
        ActionOk = false; SetError(null, null); RaiseStateChanged();
    }

    /// <summary>
    /// Loads the list of attachment categories from the API into <see cref="Items"/>.
    /// Uses <see cref="ApiClient"/> to fetch data and updates the view model state (Loading, errors).
    /// </summary>
    /// <param name="ct">Cancellation token used for the API call.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; SetError(null, null); ActionOk = false; EditId = Guid.Empty; EditName = string.Empty; RaiseStateChanged();
        try
        {
            var list = await ApiClient.Attachments_ListCategoriesAsync(ct);
            Items.Clear();
            if (list is not null)
            {
                Items.AddRange(list.OrderBy(x => x.Name));
            }
        }
        catch (Exception ex)
        {
            // Errors are captured into the view model error state instead of being thrown.
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Creates a new attachment category with the name in <see cref="NewName"/> and adds it to <see cref="Items"/> on success.
    /// If creation succeeds the <see cref="ActionOk"/> flag is set to true and <see cref="NewName"/> is cleared.
    /// Errors are captured in the view model via <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/>.
    /// </summary>
    /// <param name="ct">Cancellation token used for the API call.</param>
    /// <returns>A task representing the asynchronous add operation.</returns>
    public async Task AddAsync(CancellationToken ct = default)
    {
        var name = NewName?.Trim() ?? string.Empty;
        if (name.Length < 2) { return; }
        Busy = true; SetError(null, null); ActionOk = false; RaiseStateChanged();
        try
        {
            var dto = await ApiClient.Attachments_CreateCategoryAsync(name, ct);
            if (dto is not null)
            {
                Items.Add(dto);
                Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                NewName = string.Empty;
                ActionOk = true;
            }
        }
        catch (Exception ex)
        {
            // Errors are captured into the view model error state instead of being thrown.
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Begins editing for the category with the specified id and populates edit fields.
    /// If the view model is currently busy, the call is ignored.
    /// </summary>
    /// <param name="id">The id of the category to edit.</param>
    /// <param name="currentName">The current name of the category (used to populate the edit field).</param>
    public void BeginEdit(Guid id, string currentName)
    {
        if (Busy) { return; }
        EditId = id; EditName = currentName; SetError(null, null); ActionOk = false; RaiseStateChanged();
    }

    /// <summary>
    /// Cancels any in-progress edit and clears the edit fields.
    /// </summary>
    public void CancelEdit()
    {
        EditId = Guid.Empty; EditName = string.Empty; SetError(null, null); RaiseStateChanged();
    }

    /// <summary>
    /// Saves the current edit (<see cref="EditId"/> / <see cref="EditName"/>) by calling the API
    /// and updates the corresponding item in <see cref="Items"/> if the update succeeds.
    /// Errors are captured in the view model via <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/>.
    /// </summary>
    /// <param name="ct">Cancellation token used for the API call.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public async Task SaveEditAsync(CancellationToken ct = default)
    {
        if (EditId == Guid.Empty) { return; }
        var name = EditName?.Trim() ?? string.Empty;
        if (name.Length < 2) { return; }
        Busy = true; SetError(null, null); ActionOk = false; RaiseStateChanged();
        try
        {
            var dto = await ApiClient.Attachments_UpdateCategoryNameAsync(EditId, name, ct);
            if (dto is not null)
            {
                var idx = Items.FindIndex(x => x.Id == dto.Id);
                if (idx >= 0) { Items[idx] = dto; }
                else { Items.Add(dto); }
                Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                ActionOk = true;
                CancelEdit();
            }
        }
        catch (Exception ex)
        {
            // Errors are captured into the view model error state instead of being thrown.
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Deletes the category with the specified id by calling the API and removes it from <see cref="Items"/> on success.
    /// Errors are captured in the view model via <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/>.
    /// </summary>
    /// <param name="id">The id of the category to delete.</param>
    /// <param name="ct">Cancellation token used for the API call.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return;
        }

        Busy = true; SetError(null, null); ActionOk = false; RaiseStateChanged();
        try
        {
            var ok = await ApiClient.Attachments_DeleteCategoryAsync(id, ct);
            if (ok)
            {
                var idx = Items.FindIndex(x => x.Id == id);
                if (idx >= 0) { Items.RemoveAt(idx); }
                ActionOk = true;
            }
            else
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed");
            }
        }
        catch (Exception)
        {
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed");
        }
        finally { Busy = false; RaiseStateChanged(); }
    }
}
