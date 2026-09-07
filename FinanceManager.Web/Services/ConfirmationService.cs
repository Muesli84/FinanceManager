using FinanceManager.Shared;

namespace FinanceManager.Web.Services;

/// <summary>
/// Default implementation of <see cref="IConfirmationService"/>.
/// Reads the user's <c>ShowConfirmations</c> preference lazily from the API and caches it for the current scope.
/// When confirmations are enabled, it raises <see cref="OnShow"/> and waits for the UI to call <see cref="SetResult(bool)"/>.
/// </summary>
public sealed class ConfirmationService : IConfirmationService
{
    private readonly IApiClient _api;
    private readonly ILogger<ConfirmationService> _logger;
    private readonly object _lock = new();

    private bool? _showConfirmationsCache;
    private ConfirmationRequest? _pendingRequest;
    private TaskCompletionSource<bool>? _taskCompletionSource;

    /// <summary>
    /// Initializes a new instance of <see cref="ConfirmationService"/>.
    /// </summary>
    /// <param name="api">API client used to read the user's confirmation preference.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public ConfirmationService(IApiClient api, ILogger<ConfirmationService> logger)
    {
        _api = api;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ConfirmationRequest>? OnShow;

    /// <inheritdoc />
    public event EventHandler? OnChanged;

    /// <inheritdoc />
    public ConfirmationRequest? CurrentRequest
    {
        get
        {
            lock (_lock)
            {
                return _pendingRequest;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var showConfirmations = await GetShowConfirmationsAsync(ct);
        if (!showConfirmations)
        {
            _logger.LogInformation("Confirmation suppressed for action {Action}; user disabled confirmations.", request.ContextId ?? request.TitleResourceKey);
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        lock (_lock)
        {
            _pendingRequest = request;
            _taskCompletionSource = tcs;
        }

        using (ct.Register(() => tcs.TrySetCanceled()))
        {
            RaiseOnShow(request);
            try
            {
                return await tcs.Task;
            }
            finally
            {
                bool changed;
                lock (_lock)
                {
                    changed = _taskCompletionSource == tcs;
                    if (changed)
                    {
                        _pendingRequest = null;
                        _taskCompletionSource = null;
                    }
                }

                if (changed)
                {
                    OnChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    /// <inheritdoc />
    public void SetResult(bool confirmed)
    {
        ConfirmationRequest? request;
        TaskCompletionSource<bool>? tcs;
        lock (_lock)
        {
            request = _pendingRequest;
            tcs = _taskCompletionSource;
            _pendingRequest = null;
            _taskCompletionSource = null;
        }

        if (tcs is null)
        {
            return;
        }

        if (confirmed)
        {
            _logger.LogInformation("Confirmation accepted for action {Action}.", request?.ContextId ?? request?.TitleResourceKey ?? "unknown");
        }

        tcs.TrySetResult(confirmed);
        OnChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Cancel() => SetResult(false);

    /// <inheritdoc />
    public Task InvalidateCacheAsync()
    {
        lock (_lock)
        {
            _showConfirmationsCache = null;
        }

        return Task.CompletedTask;
    }

    private async Task<bool> GetShowConfirmationsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_showConfirmationsCache.HasValue)
            {
                return _showConfirmationsCache.Value;
            }
        }

        try
        {
            var profile = await _api.UserSettings_GetProfileAsync(ct);
            var value = profile?.ShowConfirmations ?? true;
            lock (_lock)
            {
                _showConfirmationsCache = value;
            }

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user confirmation preference; defaulting to enabled.");
            return true;
        }
    }

    private void RaiseOnShow(ConfirmationRequest request)
    {
        try
        {
            OnShow?.Invoke(this, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to raise confirmation dialog show event.");
        }

        OnChanged?.Invoke(this, EventArgs.Empty);
    }
}
