using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class UsersViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public UsersViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    // State
    public bool Loaded { get; private set; }
    public string? Error { get; set; }
    public bool BusyCreate { get; private set; }
    public bool BusyRow { get; private set; }

    public List<UserVm> Users { get; } = new();

    // Create form model
    public CreateVm Create { get; private set; } = new();

    // Edit buffer
    public UserVm? Edit { get; private set; }
    public string EditUsername { get; set; } = string.Empty;
    public bool EditIsAdmin { get; set; }
    public bool EditActive { get; set; }

    // Last reset info
    public Guid LastResetUserId { get; private set; }
    public string? LastResetPassword { get; private set; }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            Error = null;
            var data = await _http.GetFromJsonAsync<List<UserVm>>("/api/admin/users", ct);
            Users.Clear();
            if (data != null) { Users.AddRange(data); }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        RaiseStateChanged();
    }

    public void BeginEdit(UserVm u)
    {
        Edit = u;
        EditUsername = u.Username;
        EditIsAdmin = u.IsAdmin;
        EditActive = u.Active;
        RaiseStateChanged();
    }

    public void CancelEdit()
    {
        Edit = null;
        RaiseStateChanged();
    }

    public void SetEditUsername(string value) { EditUsername = value; }
    public void SetEditIsAdmin(bool value) { EditIsAdmin = value; }
    public void SetEditActive(bool value) { EditActive = value; }

    public async Task SaveEditAsync(Guid id, CancellationToken ct = default)
    {
        if (Edit == null) { return; }
        BusyRow = true; Error = null; RaiseStateChanged();
        try
        {
            var req = new UpdateUserRequest { Username = EditUsername, IsAdmin = EditIsAdmin, Active = EditActive };
            using var resp = await _http.PutAsJsonAsync($"/api/admin/users/{id}", req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var updated = await resp.Content.ReadFromJsonAsync<UserVm>(cancellationToken: ct);
                var idx = Users.FindIndex(x => x.Id == id);
                if (idx >= 0 && updated != null) { Users[idx] = updated; }
                Edit = null;
            }
            else
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            BusyRow = false; RaiseStateChanged();
        }
    }

    public async Task CreateAsync(CancellationToken ct = default)
    {
        BusyCreate = true; Error = null; RaiseStateChanged();
        try
        {
            var req = new CreateUserRequest { Username = Create.Username.Trim(), Password = Create.Password, IsAdmin = Create.IsAdmin };
            using var resp = await _http.PostAsJsonAsync("/api/admin/users", req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var created = await resp.Content.ReadFromJsonAsync<UserVm>(cancellationToken: ct);
                if (created != null) { Users.Add(created); }
                Create = new();
            }
            else
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            BusyCreate = false; RaiseStateChanged();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        BusyRow = true; RaiseStateChanged();
        try
        {
            using var resp = await _http.DeleteAsync($"/api/admin/users/{id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                Users.RemoveAll(u => u.Id == id);
            }
            else { Error = await resp.Content.ReadAsStringAsync(ct); }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { BusyRow = false; RaiseStateChanged(); }
    }

    public async Task ResetPasswordAsync(Guid id, CancellationToken ct = default)
    {
        var newPw = Guid.NewGuid().ToString("N")[..12];
        BusyRow = true; RaiseStateChanged();
        try
        {
            using var resp = await _http.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", new ResetPasswordRequest { NewPassword = newPw }, ct);
            if (resp.IsSuccessStatusCode) { LastResetUserId = id; LastResetPassword = newPw; }
            else { Error = await resp.Content.ReadAsStringAsync(ct); }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { BusyRow = false; RaiseStateChanged(); }
    }

    public async Task UnlockAsync(Guid id, CancellationToken ct = default)
    {
        BusyRow = true; RaiseStateChanged();
        try
        {
            using var resp = await _http.PostAsync($"/api/admin/users/{id}/unlock", content: null, ct);
            if (resp.IsSuccessStatusCode)
            {
                var found = Users.FirstOrDefault(x => x.Id == id);
                if (found != null) { found.LockedUntilUtc = null; }
            }
            else { Error = await resp.Content.ReadAsStringAsync(ct); }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { BusyRow = false; RaiseStateChanged(); }
    }

    public void ClearLastPassword()
    {
        LastResetUserId = Guid.Empty; LastResetPassword = null; RaiseStateChanged();
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var groups = new List<UiRibbonGroup>();
        var nav = new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        });
        groups.Add(nav);

        var actionsItems = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Reload"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, false, "Reload")
        };
        if (Edit != null)
        {
            actionsItems.Add(new UiRibbonItem(localizer["Ribbon_CancelEdit"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, "CancelEdit"));
        }
        if (LastResetUserId != Guid.Empty && !string.IsNullOrEmpty(LastResetPassword))
        {
            actionsItems.Add(new UiRibbonItem(localizer["Ribbon_HidePassword"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, "HidePassword"));
        }
        groups.Add(new UiRibbonGroup(localizer["Ribbon_Group_Actions"], actionsItems));
        return groups;
    }

    // DTOs
    public sealed class CreateVm
    {
        [Required, MinLength(3)] public string Username { get; set; } = string.Empty;
        [Required, MinLength(6)] public string Password { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }
    public sealed class CreateUserRequest { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; public bool IsAdmin { get; set; } }
    public sealed class UpdateUserRequest { public string? Username { get; set; } public bool? IsAdmin { get; set; } public bool? Active { get; set; } }
    public sealed class ResetPasswordRequest { public string NewPassword { get; set; } = string.Empty; }
    public sealed class UserVm { public Guid Id { get; set; } public string Username { get; set; } = string.Empty; public bool IsAdmin { get; set; } public bool Active { get; set; } public DateTime? LockedUntilUtc { get; set; } public DateTime LastLoginUtc { get; set; } public string? PreferredLanguage { get; set; } }
}
