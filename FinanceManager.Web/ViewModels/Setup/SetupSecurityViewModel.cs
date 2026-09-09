using FinanceManager.Application;
using FinanceManager.Shared;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model used in the setup area to manage IP blocks and related security operations.
/// Provides listing, creation, blocking/unblocking and administration helpers for IP blocks.
/// </summary>
public sealed class SetupSecurityViewModel : BaseViewModel
{
    private readonly ICurrentUserService _current;

    /// <summary>
    /// Initializes a new instance of <see cref="SetupSecurityViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services such as <see cref="ICurrentUserService"/>.</param>
    public SetupSecurityViewModel(IServiceProvider sp) : base(sp)
    {
        _current = sp.GetRequiredService<ICurrentUserService>();
    }

    /// <summary>
    /// Represents a single IP block row shown in the admin UI.
    /// </summary>
    public sealed class IpBlockItem
    {
        /// <summary>Identifier of the IP block record.</summary>
        public Guid Id { get; set; }
        /// <summary>Blocked IP address (or candidate IP) as string.</summary>
        public string IpAddress { get; set; } = string.Empty;
        /// <summary>True when the IP is currently blocked.</summary>
        public bool IsBlocked { get; set; }
        /// <summary>UTC timestamp when the IP was blocked.</summary>
        public DateTime? BlockedAtUtc { get; set; }
        /// <summary>Optional reason for blocking the IP.</summary>
        public string? BlockReason { get; set; }
        /// <summary>Number of failed attempts from unknown users recorded for this IP.</summary>
        public int UnknownUserFailedAttempts { get; set; }
        /// <summary>UTC time of the last failed attempt by an unknown user, if any.</summary>
        public DateTime? UnknownUserLastFailedUtc { get; set; }
        /// <summary>UTC creation timestamp of the IP block record.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>UTC last modified timestamp of the IP block record, if any.</summary>
        public DateTime? ModifiedUtc { get; set; }
    }

    /// <summary>
    /// Current list of IP blocks shown in the UI.
    /// </summary>
    /// <returns>The result.</returns>
    public List<IpBlockItem> Items { get; private set; } = new();

    /// <summary>
    /// Indicates whether the view model is busy performing a create/block/unblock/delete operation.
    /// </summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// IP address input bound to the UI when creating a new block.
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// Optional reason text supplied when creating a new block.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When true, newly created entries will be blocked immediately upon creation.
    /// </summary>
    public bool BlockOnCreate { get; set; } = true;

    /// <summary>
    /// Indicates whether the current user has administrative privileges required to manage IP blocks.
    /// </summary>
    public bool IsAdmin => _current.IsAdmin;

    /// <summary>
    /// Reloads the list of IP blocks from the API and updates the internal <see cref="Items"/> collection.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the reload has finished.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the provided cancellation token is cancelled.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the current user is not an administrator (via <see cref="CheckAdmin"/>).</exception>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            CheckAuthentication();
            CheckAdmin();
            SetError(null, null);
            var list = await ApiClient.Admin_ListIpBlocksAsync(null, ct);
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
            SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? ex.Message);
        }
        finally { RaiseStateChanged(); }
    }

    /// <summary>
    /// Ensures the current user is an administrator and throws <see cref="UnauthorizedAccessException"/> otherwise.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown when the current user is not an administrator.</exception>
    private void CheckAdmin()
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Error_AdminAccessRequired");
    }

    /// <summary>
    /// Creates a new IP block record using the current <see cref="Ip"/>, <see cref="Reason"/> and <see cref="BlockOnCreate"/> values.
    /// Resets input fields and reloads the list on success.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when the create operation has finished.</returns>
    public async Task CreateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Ip)) { SetError("Error_IpRequired", ""); RaiseStateChanged(); return; }
        Busy = true; RaiseStateChanged();
        try
        {
            var req = new IpBlockCreateRequest(Ip.Trim(), Reason, BlockOnCreate);
            var _ = await ApiClient.Admin_CreateIpBlockAsync(req, ct);
            Ip = string.Empty; Reason = null; BlockOnCreate = true;
            await ReloadAsync(ct);
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? ex.Message);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Blocks the IP block identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Identifier of the IP block to block.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation has finished. Swallows exceptions and reloads the list afterwards.</returns>
    public async Task BlockAsync(Guid id, CancellationToken ct = default)
    {
        try { await ApiClient.Admin_BlockIpAsync(id, null, ct); }
        catch { }
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Unblocks the IP block identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Identifier of the IP block to unblock.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation has finished. Swallows exceptions and reloads the list afterwards.</returns>
    public async Task UnblockAsync(Guid id, CancellationToken ct = default)
    {
        try { await ApiClient.Admin_UnblockIpAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Resets failure counters for the IP block identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Identifier of the IP block whose counters should be reset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation has finished. Swallows exceptions and reloads the list afterwards.</returns>
    public async Task ResetCountersAsync(Guid id, CancellationToken ct = default)
    {
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Finalize_Title",
            MessageResourceKey: "Confirmation_Finalize_Message",
            Severity: ConfirmationSeverity.Warning)))
        {
            return;
        }

        try { await ApiClient.Admin_ResetCountersAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Deletes the IP block identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Identifier of the IP block to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation has finished. Swallows exceptions and reloads the list afterwards.</returns>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return;
        }

        try { await ApiClient.Admin_DeleteIpBlockAsync(id, ct); }
        catch { }
        await ReloadAsync(ct);
    }
}
