using FinanceManager.Application;
using FinanceManager.Shared; // IApiClient

namespace FinanceManager.Web.ViewModels;

public sealed class SetupIpBlocksViewModel : ViewModelBase
{
    private readonly IApiClient _api;
    private readonly ICurrentUserService _current;

    public SetupIpBlocksViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
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
            var list = await _api.Admin_ListIpBlocksAsync(null, ct);
            Items = (list ?? Array.Empty<IpBlockDto>())
                .Select(d => new IpBlockItem
                {
                    Id = d.Id,
                    IpAddress = d.IpAddress,
                    IsBlocked = d.IsBlocked,
                    BlockedAtUtc = d.BlockedAtUtc,
                    BlockReason = d.BlockReason,
                    UnknownUserFailedAttempts = d.UnknownUserFailedAttempts,
                    UnknownUserLastFailedUtc = d.UnknownUserLastFailedUtc,
                    CreatedUtc = d.CreatedUtc,
                    ModifiedUtc = d.ModifiedUtc
                })
                .ToList();
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
            var req = new IpBlockCreateRequest(Ip.Trim(), Reason, BlockOnCreate);
            var _ = await _api.Admin_CreateIpBlockAsync(req, ct);
            Ip = string.Empty; Reason = null; BlockOnCreate = true;
            await ReloadAsync(ct);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    public async Task BlockAsync(Guid id, CancellationToken ct = default)
    {
        try { await _api.Admin_BlockIpAsync(id, null, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task UnblockAsync(Guid id, CancellationToken ct = default)
    {
        try { await _api.Admin_UnblockIpAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task ResetCountersAsync(Guid id, CancellationToken ct = default)
    {
        try { await _api.Admin_ResetCountersAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try { await _api.Admin_DeleteIpBlockAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }
}
