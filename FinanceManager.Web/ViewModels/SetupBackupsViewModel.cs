using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManager.Web.ViewModels;

public sealed class SetupBackupsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SetupBackupsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public sealed class BackupItem
    {
        public Guid Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public List<BackupItem>? Backups { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public bool HasActiveRestore { get; private set; }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadBackupsAsync(ct);
    }

    public async Task LoadBackupsAsync(CancellationToken ct = default)
    {
        try
        {
            Error = null;
            var list = await _http.GetFromJsonAsync<List<BackupItem>>("/api/setup/backups", ct);
            Backups = list ?? new List<BackupItem>();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Backups = new List<BackupItem>();
        }
        finally { RaiseStateChanged(); }
    }

    public async Task CreateAsync(CancellationToken ct = default)
    {
        Busy = true; Error = null; RaiseStateChanged();
        try
        {
            using var resp = await _http.PostAsync("/api/setup/backups", content: null, ct);
            if (resp.IsSuccessStatusCode)
            {
                var created = await resp.Content.ReadFromJsonAsync<BackupItem>(cancellationToken: ct);
                if (created is not null)
                {
                    Backups ??= new List<BackupItem>();
                    Backups.Insert(0, created);
                }
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
        finally { Busy = false; RaiseStateChanged(); }
    }

    public async Task StartApplyAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) { return; }
        try
        {
            using var resp = await _http.PostAsync($"/api/setup/backups/{id}/apply/start", content: null, ct);
            if (resp.IsSuccessStatusCode)
            {
                HasActiveRestore = true;
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
        finally { RaiseStateChanged(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Busy = true; Error = null; RaiseStateChanged();
        try
        {
            using var resp = await _http.DeleteAsync($"/api/setup/backups/{id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                if (Backups is not null)
                {
                    Backups.RemoveAll(x => x.Id == id);
                }
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
        finally { Busy = false; RaiseStateChanged(); }
    }

    public async Task UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Busy = true; Error = null; RaiseStateChanged();
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(stream), "file", fileName);
            using var resp = await _http.PostAsync("/api/setup/backups/upload", content, ct);
            if (resp.IsSuccessStatusCode)
            {
                var created = await resp.Content.ReadFromJsonAsync<BackupItem>(cancellationToken: ct);
                if (created is not null)
                {
                    AddBackup(created);
                }
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
        finally { Busy = false; RaiseStateChanged(); }
    }

    public void AddBackup(BackupItem item)
    {
        Backups ??= new List<BackupItem>();
        Backups.Insert(0, item);
        RaiseStateChanged();
    }
}
