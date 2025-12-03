namespace FinanceManager.Web.ViewModels;

public sealed class SetupBackupsViewModel : ViewModelBase
{
    private readonly FinanceManager.Shared.IApiClient _api;

    public SetupBackupsViewModel(IServiceProvider sp, FinanceManager.Shared.IApiClient apiClient) : base(sp)
    {
        _api = apiClient;
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
            var list = await _api.Backups_ListAsync(ct);
            Backups = list?.Select(b => new BackupItem { Id = b.Id, CreatedUtc = b.CreatedUtc, FileName = b.FileName, SizeBytes = b.SizeBytes, Source = b.Source }).ToList() ?? new List<BackupItem>();
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
            var created = await _api.Backups_CreateAsync(ct);
            if (created is not null)
            {
                Backups ??= new List<BackupItem>();
                Backups.Insert(0, new BackupItem { Id = created.Id, CreatedUtc = created.CreatedUtc, FileName = created.FileName, SizeBytes = created.SizeBytes, Source = created.Source });
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
            var status = await _api.Backups_StartApplyAsync(id, ct);
            HasActiveRestore = status.Running;
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
            var ok = await _api.Backups_DeleteAsync(id, ct);
            if (ok)
            {
                if (Backups is not null)
                {
                    Backups.RemoveAll(x => x.Id == id);
                }
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
            var created = await _api.Backups_UploadAsync(stream, fileName, ct);
            if (created is not null)
            {
                AddBackup(new BackupItem { Id = created.Id, CreatedUtc = created.CreatedUtc, FileName = created.FileName, SizeBytes = created.SizeBytes, Source = created.Source });
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
