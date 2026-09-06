using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Users;
using Microsoft.Extensions.Localization;
using System.Reflection;
using FinanceManager.Web.ViewModels.Common;
using FinanceManager.Web.Components.Shared;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model responsible for displaying and editing a user in the admin setup area.
/// Supports creating, updating, deleting, enabling/disabling and unlocking users via the admin API.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("users")]
public sealed class UserCardViewModel : BaseCardViewModel<(string Key, string Value)>
{
    private readonly FinanceManager.Shared.IApiClient _api;

    /// <summary>
    /// Initializes a new instance of <see cref="UserCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve additional services such as localizer.</param>
    /// <param name="apiClient">API client used to call admin user endpoints. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="apiClient"/> is <c>null</c>.</exception>
    public UserCardViewModel(IServiceProvider sp, FinanceManager.Shared.IApiClient apiClient) : base(sp)
    {
        _api = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    /// <summary>
    /// Currently loaded user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// DTO representing the loaded user, or <c>null</c> when no user is loaded.
    /// </summary>
    public UserAdminDto? User { get; private set; }

    /// <summary>
    /// Title shown on the card header. Falls back to a localized "Users_Title" resource when no username is available.
    /// </summary>
    public override string Title => User?.Username ?? Localizer?["Users_Title"] ?? "User";

    /// <summary>
    /// Initializes the view model for the given id by delegating to <see cref="LoadAsync(Guid, CancellationToken)"/>.
    /// </summary>
    /// <param name="id">Identifier of the user to initialize the view model for.</param>
    public override async Task InitializeAsync(Guid id)
    {
        await LoadAsync(id);
    }

    /// <summary>
    /// Loads the user for the supplied <paramref name="id"/>. When <see cref="Guid.Empty"/> a new unsaved user model is prepared.
    /// </summary>
    /// <param name="id">Identifier of the user to load or <see cref="Guid.Empty"/> for create mode.</param>
    /// <param name="ct">Cancellation token to cancel API calls.</param>
    /// <returns>A task that completes when loading has finished.</returns>
    /// <exception cref="OperationCanceledException">May be thrown when the provided cancellation token is cancelled by the caller.</exception>
    public async Task LoadAsync(Guid id, CancellationToken ct = default)
    {
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            UserId = id;
            if (id == Guid.Empty)
            {
                // New user: initialize defaults and apply any prefill
                var name = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill : string.Empty;

                // Create UserAdminDto via reflection to avoid depending on exact ctor signature
                var udtType = typeof(UserAdminDto);
                var ctor = udtType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor != null)
                {
                    var parms = ctor.GetParameters();
                    var args = new object?[parms.Length];
                    for (int i = 0; i < parms.Length; i++)
                    {
                        var p = parms[i];
                        if (p.ParameterType == typeof(Guid) || p.ParameterType == typeof(Guid?)) args[i] = Guid.Empty;
                        else if (p.ParameterType == typeof(string)) args[i] = name;
                        else if (p.ParameterType == typeof(bool) || p.ParameterType == typeof(bool?)) args[i] = false;
                        else if (p.ParameterType == typeof(DateTime) || p.ParameterType == typeof(DateTime?)) args[i] = (object?)null ?? default(DateTime);
                        else args[i] = null;
                    }
                    try { User = (UserAdminDto?)ctor.Invoke(args); } catch { User = null; }
                }

                // Build card record for new user (username editable)
                CardRecord = await BuildCardRecordAsync(User);

                Loading = false;
                RaiseStateChanged();
                return;
            }

            var u = await _api.Admin_GetUserAsync(id, ct);
            if (u != null) User = u; else SetError("Err_NotFound", Localizer?["Err_NotFound"].Value ?? "Not found");

            CardRecord = await BuildCardRecordAsync(User);
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Helper to read a text value from the current <see cref="CardRecord"/> by label key.
    /// </summary>
    /// <param name="key">Label key of the card field.</param>
    /// <param name="fallback">Optional fallback returned when the field is missing or empty.</param>
    /// <returns>The text value or the provided fallback / empty string.</returns>
    private string GetFieldText(string key, string? fallback = null)
    {
        if (CardRecord?.Fields != null)
        {
            var f = CardRecord.Fields.FirstOrDefault(x => string.Equals(x.LabelKey, key, StringComparison.OrdinalIgnoreCase));
            if (f != null && !string.IsNullOrWhiteSpace(f.Text)) return f.Text;
        }
        return fallback ?? string.Empty;
    }

    /// <summary>
    /// Helper to read a boolean value from the current <see cref="CardRecord"/> by label key.
    /// </summary>
    /// <param name="key">Label key of the card field.</param>
    /// <param name="fallback">Optional fallback when the field is missing.</param>
    /// <returns>Boolean value or the provided fallback (defaults to <c>false</c>).</returns>
    private bool GetFieldBoolean(string key, bool? fallback = null)
    {
        if (CardRecord?.Fields != null)
        {
            var f = CardRecord.Fields.FirstOrDefault(x => string.Equals(x.LabelKey, key, StringComparison.OrdinalIgnoreCase));
            if (f != null && f.BoolValue.HasValue) return f.BoolValue.Value;
        }
        return fallback ?? false;
    }

    /// <summary>
    /// Creates or updates the user using the admin API. When creating a user a random password is generated.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>True when the save operation succeeded; otherwise false.</returns>
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        if (User == null) return false;
        try
        {
            var preferredLang = User.PreferredLanguage ?? string.Empty;
            var createUser = UserId == Guid.Empty;

            // Pick up edited username from CardRecord (apply pending UI changes)
            var usernameToUse = GetFieldText("Card_Caption_User_Username", User.Username);
            // Pick up admin flag from CardRecord (editable during creation)
            var isAdminToUse = GetFieldBoolean("Card_Caption_User_IsAdmin", User.IsAdmin);

            if (createUser)
            {
                // Create user - password is generated here (will be shown/hinted elsewhere if needed)
                var created = await _api.Admin_CreateUserAsync(new CreateUserRequest(usernameToUse, Guid.NewGuid().ToString(), isAdminToUse), ct);
                if (created == null)
                    return false;
                User = created;
                CardRecord = await BuildCardRecordAsync(User);
            }

            // Update (or update after create)
            var updated = await _api.Admin_UpdateUserAsync(User.Id, new UpdateUserRequest(usernameToUse, isAdminToUse, User.Active, preferredLang), ct);
            if (updated == null)
                return false;
            User = updated;
            CardRecord = await BuildCardRecordAsync(User);
            if (createUser)
                RaiseUiActionRequested("Saved", User.Id.ToString());
            return true;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
        }
        return false;
    }

    /// <summary>
    /// Deletes the currently loaded user via the admin API.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the delete operation.</param>
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (User == null) return false;
        try
        {
            var ok = await _api.Admin_DeleteUserAsync(User.Id, ct);
            if (ok) RaiseUiActionRequested("Deleted");
            return ok;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Enables (activates) the currently loaded user via the admin API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the operation succeeded; otherwise false.</returns>
    public async Task<bool> EnableAsync(CancellationToken ct = default)
    {
        if (User == null) return false;
        try
        {
            var preferredLang = User.PreferredLanguage ?? string.Empty;
            var updated = await _api.Admin_UpdateUserAsync(User.Id, new UpdateUserRequest(User.Username, User.IsAdmin, true, preferredLang), ct);
            if (updated != null) { User = updated; CardRecord = await BuildCardRecordAsync(User); RaiseStateChanged(); return true; }
            return false;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Disables (deactivates) the currently loaded user via the admin API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the operation succeeded; otherwise false.</returns>
    public async Task<bool> DisableAsync(CancellationToken ct = default)
    {
        if (User == null) return false;
        try
        {
            var preferredLang = User.PreferredLanguage ?? string.Empty;
            var updated = await _api.Admin_UpdateUserAsync(User.Id, new UpdateUserRequest(User.Username, User.IsAdmin, false, preferredLang), ct);
            if (updated != null) { User = updated; CardRecord = await BuildCardRecordAsync(User); RaiseStateChanged(); return true; }
            return false;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Unlocks the currently loaded user if it is locked/blocked.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the unlock succeeded; otherwise false.</returns>
    public async Task<bool> UnblockAsync(CancellationToken ct = default)
    {
        if (User == null) return false;
        try
        {
            var ok = await _api.Admin_UnlockUserAsync(User.Id, ct);
            if (ok) await LoadAsync(User.Id, ct);
            return ok;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode, _api.LastError ?? ex.Message);
            return false;
        }
    }

    // BaseCardViewModel abstract implementations
    /// <summary>
    /// Loads the user for the provided id. This implementation delegates to the overload that accepts a cancellation token.
    /// </summary>
    /// <param name="id">Identifier of the user to load.</param>
    public override async Task LoadAsync(Guid id)
    {
        await LoadAsync(id, CancellationToken.None);
    }

    /// <summary>
    /// Users do not support symbol uploads in the current model.
    /// </summary>
    /// <returns><c>false</c> always.</returns>
    protected override bool IsSymbolUploadAllowed()
    {
        // Users do not support symbol attachments in current model
        return false;
    }

    /// <summary>
    /// Returns the attachment parent kind and id for symbol uploads. Returns <see cref="AttachmentEntityKind.None"/>.
    /// </summary>
    /// <returns>Tuple containing <see cref="AttachmentEntityKind.None"/> and <see cref="Guid.Empty"/>.</returns>
    protected override SymbolParentRef GetSymbolParent()
    {
        return new(AttachmentEntityKind.None, Guid.Empty);
    }

    /// <summary>
    /// AssignNewSymbol is a no-op for users; exceptions are swallowed.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign or <c>null</c> to clear.</param>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        // No-op for users; swallow exceptions
        await Task.CompletedTask;
    }

    private static string FormatDate(object? val)
    {
        if (val is DateTime dt)
            if (dt == DateTime.MinValue)
                return "";
            else
                return dt.ToString("u");
        return "";
    }

    /// <summary>
    /// Builds the card record displayed in the UI based on the provided <see cref="UserAdminDto"/>.
    /// </summary>
    /// <param name="u">User DTO or <c>null</c> when creating a new user.</param>
    /// <returns>A <see cref="CardRecord"/> representing the user's editable and read-only fields.</returns>
    private async Task<CardRecord> BuildCardRecordAsync(UserAdminDto? u)
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        var fields = new List<CardField>
        {
            // Username: editable only when creating (UserId == Guid.Empty)
            new CardField("Card_Caption_User_Username", CardFieldKind.Text, text: u?.Username ?? string.Empty, editable: (UserId == Guid.Empty)),
            // Admin flag: editable when creating, read-only for existing users
            new CardField("Card_Caption_User_IsAdmin", CardFieldKind.Boolean, boolValue: u?.IsAdmin, text: u != null && u.IsAdmin ? "✓" : "", editable: (UserId == Guid.Empty)),
            // Active flag: not editable
            new CardField("Card_Caption_User_Active", CardFieldKind.Boolean, boolValue: u?.Active, text: u != null && u.Active ? "✓" : "", editable: false),
            // Locked until / blocked info
            new CardField("Card_Caption_User_LockedUntil", CardFieldKind.Text, text: FormatDate(u == null ? null : (object?) (u.LockoutEnd)), editable: false),
            // Last login
            new CardField("Card_Caption_User_LastLogin", CardFieldKind.Text, text: FormatDate(u == null ? null : (object?) (u.LastLoginUtc)), editable: false)
        };

        var record = new CardRecord(fields, u);
        return ApplyPendingValues(record);
    }

    /// <summary>
    /// Builds ribbon register definitions for the user card including navigation and management actions such as Save, Delete, Activate/Deactivate, Unblock and SetPassword.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve the labels for ribbon actions.</param>
    /// <returns>A list of ribbon registers describing available actions for the current view.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
        };

        var manage = new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, User != null && User.Id != Guid.Empty, null, async () => { await SaveAsync(); }) { MobileShortcut = true },
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, !(UserId != Guid.Empty && User != null), null, async () => { await DeleteAsync(); })
        };

        // Activate/Deactivate
        if (User != null)
        {
            if (User.IsAdmin == false) // don't allow toggling admin flag; we toggle active state
            {
                if (User.Active)
                {
                    manage.Add(new UiRibbonAction("Deactivate", localizer["Ribbon_Deactivate"].Value, "<svg><use href='/icons/sprite.svg#archive'/></svg>", UiRibbonItemSize.Small, !(UserId != Guid.Empty && User != null), null, async () => { await DisableAsync(); }));
                }
                else
                {
                    manage.Add(new UiRibbonAction("Activate", localizer["Ribbon_Activate"].Value, "<svg><use href='/icons/sprite.svg#check'/></svg>", UiRibbonItemSize.Small, !(UserId != Guid.Empty && User != null), null, async () => { await EnableAsync(); }));
                }
            }

            // Unblock if locked (User is non-null here, guarded by the enclosing "if (User != null)" above;
            // the compiler cannot track that narrowing across the nested if/else block for a property)
            if (User!.LockoutEnd != null && User.LockoutEnd > DateTime.UtcNow)
            {
                manage.Add(new UiRibbonAction("Unblock", localizer["Ribbon_Unblock"].Value, "<svg><use href='/icons/sprite.svg#unlock'/></svg>", UiRibbonItemSize.Small, !(UserId != Guid.Empty && User != null), null, async () => { await UnblockAsync(); }));
            }

            // Set password action for existing users
            if (UserId != Guid.Empty && User != null)
            {
                var specParams = new Dictionary<string, object?>
                {
                    ["UserId"] = User.Id,
                    ["OverlayTitle"] = localizer["Users_SetPassword_Title"].Value
                };
                manage.Add(new UiRibbonAction("SetPassword", localizer["Ribbon_SetPassword"].Value, "<svg><use href='/icons/sprite.svg#key'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("SetPassword", new BaseViewModel.UiOverlaySpec(typeof(SetPasswordOverlay), specParams)); return Task.CompletedTask; }));
            }
        }

        var tabs = new List<UiRibbonTab>
        {
            new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, actions),
            new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, manage)
        };

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }
}
