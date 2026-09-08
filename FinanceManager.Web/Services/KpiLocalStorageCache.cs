using Microsoft.JSInterop;
using System.Text.Json;

namespace FinanceManager.Web.Services;

/// <summary>
/// Context that controls whether and for which user the local storage KPI cache operates.
/// </summary>
/// <param name="Enabled">Whether KPI caching is currently active.</param>
/// <param name="UserId">Optional user identifier used to scope local storage keys.</param>
/// <returns>The result.</returns>
public sealed record KpiLocalStorageContext(bool Enabled, string? UserId);

/// <summary>
/// Cache for home page KPI data in the browser's local storage.
/// All keys are prefixed so the cache can be cleared without touching other applications.
/// </summary>
public interface IKpiLocalStorageCache
{
    /// <summary>
    /// Gets the currently configured context for this cache instance.
    /// </summary>
    KpiLocalStorageContext Context { get; }

    /// <summary>
    /// Sets the context (enable flag and optional user id) for subsequent cache operations.
    /// </summary>
    /// <param name="context">The context to apply.</param>
    void SetContext(KpiLocalStorageContext context);

    /// <summary>
    /// Reads a typed value from the local storage cache, or <c>default</c> when missing or unreadable.
    /// </summary>
    /// <typeparam name="T">The type parameter.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores a typed value in the local storage cache. No-op when the context is disabled.
    /// </summary>
    /// <typeparam name="T">The type parameter.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>
    /// Removes a single cache entry.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries owned by this application (or the current user when a user id was set).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementation of <see cref="IKpiLocalStorageCache"/> using the browser's local storage
/// accessed through <see cref="IJSRuntime"/>.
/// </summary>
public sealed class KpiLocalStorageCache : IKpiLocalStorageCache
{
    private const string KeyPrefix = "fm.kpi.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    private readonly IJSRuntime _js;
    private KpiLocalStorageContext _context = new(false, null);

    /// <summary>
    /// Initializes a new instance of <see cref="KpiLocalStorageCache"/>.
    /// </summary>
    /// <param name="js">The JS runtime used to access the browser's local storage.</param>
    public KpiLocalStorageCache(IJSRuntime js)
    {
        _js = js;
    }

    /// <inheritdoc />
    public KpiLocalStorageContext Context => _context;

    /// <inheritdoc />
    public void SetContext(KpiLocalStorageContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_context.Enabled)
        {
            return default;
        }

        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", ct, FullKey(key));
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        if (!_context.Enabled || value is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _js.InvokeVoidAsync("localStorage.setItem", ct, FullKey(key), json);
        }
        catch
        {
            // Intentionally ignored: local storage failures should not break the UI.
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", ct, FullKey(key));
        }
        catch
        {
            // Intentionally ignored.
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAllAsync(CancellationToken ct = default)
    {
        try
        {
            var prefixToClear = _context.UserId is { } uid ? $"{KeyPrefix}{uid}." : KeyPrefix;
            var script = $"Object.keys(localStorage).filter(k => k.startsWith('{prefixToClear.Replace("'", "\\'")}'))";
            var keys = await _js.InvokeAsync<string[]>("eval", ct, script);

            foreach (var k in keys)
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", ct, k);
            }
        }
        catch
        {
            // Intentionally ignored.
        }
    }

    private string FullKey(string key)
    {
        return _context.UserId is { } uid ? $"{KeyPrefix}{uid}.{key}" : $"{KeyPrefix}{key}";
    }
}
