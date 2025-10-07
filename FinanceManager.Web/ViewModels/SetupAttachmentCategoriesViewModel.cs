using System.Net.Http.Json;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManager.Web.ViewModels;

public sealed class SetupAttachmentCategoriesViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SetupAttachmentCategoriesViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public List<AttachmentCategoryDto> Items { get; } = new();

    public bool Loading { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public string? ActionError { get; private set; }
    public bool ActionOk { get; private set; }

    public string NewName { get; set; } = string.Empty;
    public bool CanAdd => !string.IsNullOrWhiteSpace(NewName) && NewName.Trim().Length >= 2;

    public Guid EditId { get; private set; }
    public string EditName { get; set; } = string.Empty;
    public bool CanSaveEdit => !string.IsNullOrWhiteSpace(EditName) && EditName.Trim().Length >= 2;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
    }

    public void OnChanged()
    {
        ActionOk = false; ActionError = null; RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; ActionError = null; ActionOk = false; EditId = Guid.Empty; EditName = string.Empty; RaiseStateChanged();
        try
        {
            var list = await _http.GetFromJsonAsync<List<AttachmentCategoryDto>>("/api/attachments/categories", ct);
            Items.Clear();
            if (list is not null)
            {
                Items.AddRange(list.OrderBy(x => x.Name));
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    public async Task AddAsync(CancellationToken ct = default)
    {
        var name = NewName?.Trim() ?? string.Empty;
        if (name.Length < 2) { return; }
        Busy = true; ActionError = null; ActionOk = false; RaiseStateChanged();
        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/attachments/categories", new { Name = name }, ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<AttachmentCategoryDto>(cancellationToken: ct);
                if (dto is not null)
                {
                    Items.Add(dto);
                    Items.Sort((a,b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    NewName = string.Empty;
                    ActionOk = true;
                }
            }
            else
            {
                ActionError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            ActionError = ex.Message;
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    public void BeginEdit(Guid id, string currentName)
    {
        if (Busy) { return; }
        EditId = id; EditName = currentName; ActionError = null; ActionOk = false; RaiseStateChanged();
    }

    public void CancelEdit()
    {
        EditId = Guid.Empty; EditName = string.Empty; ActionError = null; RaiseStateChanged();
    }

    public async Task SaveEditAsync(CancellationToken ct = default)
    {
        if (EditId == Guid.Empty) { return; }
        var name = EditName?.Trim() ?? string.Empty;
        if (name.Length < 2) { return; }
        Busy = true; ActionError = null; ActionOk = false; RaiseStateChanged();
        try
        {
            using var resp = await _http.PutAsJsonAsync($"/api/attachments/categories/{EditId}", new { Name = name }, ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<AttachmentCategoryDto>(cancellationToken: ct);
                if (dto is not null)
                {
                    var idx = Items.FindIndex(x => x.Id == dto.Id);
                    if (idx >= 0) { Items[idx] = dto; }
                    else { Items.Add(dto); }
                    Items.Sort((a,b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    ActionOk = true;
                    CancelEdit();
                }
            }
            else
            {
                ActionError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            ActionError = ex.Message;
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Busy = true; ActionError = null; ActionOk = false; RaiseStateChanged();
        try
        {
            using var resp = await _http.DeleteAsync($"/api/attachments/categories/{id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var idx = Items.FindIndex(x => x.Id == id);
                if (idx >= 0) { Items.RemoveAt(idx); }
                ActionOk = true;
            }
            else
            {
                ActionError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            ActionError = ex.Message;
        }
        finally { Busy = false; RaiseStateChanged(); }
    }
}
