using FinanceManager.Application;

namespace FinanceManager.Web.ViewModels;

public sealed class SetupIpBlocksViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private readonly ICurrentUserService _current;

    public SetupIpBlocksViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
        _current = sp.GetRequiredService<ICurrentUserService>();
    }

    public sealed class IpBlockItem
    {
        public Guid Id { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public bool IsBlocked { get; set; }
        public DateTime? BlockedAtUtc { get; set; }
        public string? BlockReason { get; set; }
        public int UnknownUserFailedAttempts { get; set; }
        public DateTime? UnknownUserLastFailedUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ModifiedUtc { get; set; }
    }

    public List<IpBlockItem> Items { get; private set; } = new();
    public bool Busy { get; private set; }
    public string Ip { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool BlockOnCreate { get; set; } = true;
    public string? Error { get; private set; }
    public bool IsAdmin => _current.IsAdmin;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated && IsAdmin)
        {
            await ReloadAsync(ct);
        }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            Error = null;
            var resp = await _http.GetAsync("/api/admin/ip-blocks", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<IpBlockItem>>(cancellationToken: ct);
                Items = list ?? new();
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Treat 404 as 'no items' rather than an error
                Items = new();
            }
            else
            {
                var txt = await resp.Content.ReadAsStringAsync(ct);
                Error = !string.IsNullOrWhiteSpace(txt) ? txt : $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { RaiseStateChanged(); }
    }

    public async Task CreateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Ip)) { Error = "Error_IpRequired"; RaiseStateChanged(); return; }
        Busy = true; RaiseStateChanged();
        try
        {
            var req = new { IpAddress = Ip.Trim(), Reason, IsBlocked = BlockOnCreate };
            var resp = await _http.PostAsJsonAsync("/api/admin/ip-blocks", req, ct);
            if (resp.IsSuccessStatusCode)
            {
                Ip = string.Empty; Reason = null; BlockOnCreate = true;
                await ReloadAsync(ct);
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

    public async Task BlockAsync(Guid id, CancellationToken ct = default)
    {
        try { await _http.PostAsJsonAsync($"/api/admin/ip-blocks/{id}/block", new { Reason = (string?)null }, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task UnblockAsync(Guid id, CancellationToken ct = default)
    {
        try { await _http.PostAsync($"/api/admin/ip-blocks/{id}/unblock", content: null, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task ResetCountersAsync(Guid id, CancellationToken ct = default)
    {
        try { await _http.PostAsync($"/api/admin/ip-blocks/{id}/reset-counters", content: null, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try { await _http.DeleteAsync($"/api/admin/ip-blocks/{id}", ct); }
        catch { }
        await ReloadAsync(ct);
    }
}
