using System.Net.Http.Json;
using FinanceManager.Shared.Dtos.Update;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    /// <summary>
    /// Retrieves the current update status from the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current <see cref="UpdateStatusDto"/>.</returns>
    public async Task<UpdateStatusDto> Updates_GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/update/status", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UpdateStatusDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Retrieves the current update settings from the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current <see cref="UpdateSettingsDto"/>.</returns>
    public async Task<UpdateSettingsDto> Updates_GetSettingsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/update/settings", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UpdateSettingsDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Saves the update settings on the server.
    /// </summary>
    /// <param name="request">Settings update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="UpdateSettingsDto"/>.</returns>
    public async Task<UpdateSettingsDto> Updates_UpdateSettingsAsync(UpdateSettingsUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync("/api/setup/update/settings", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UpdateSettingsDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Retrieves the names of known services that can be updated.
    /// </summary>
    /// <param name="query">Optional search filter for service names.</param>
    /// <param name="take">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of service names.</returns>
    public async Task<IReadOnlyList<string>> Updates_GetServiceNamesAsync(string? query, int take = 20, CancellationToken ct = default)
    {
        var url = $"/api/setup/update/services?take={take}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&query={Uri.EscapeDataString(query)}";
        }

        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<IReadOnlyList<string>>(cancellationToken: ct)) ?? Array.Empty<string>();
    }

    /// <summary>
    /// Triggers an update check on the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="UpdateCheckResultDto"/> result of the check.</returns>
    public async Task<UpdateCheckResultDto> Updates_CheckAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/setup/update/check", content: null, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UpdateCheckResultDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Schedules the automatic update installation time.
    /// </summary>
    /// <param name="request">Schedule request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="UpdateSettingsDto"/>.</returns>
    public async Task<UpdateSettingsDto> Updates_ScheduleAsync(UpdateScheduleRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/setup/update/schedule", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UpdateSettingsDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Starts the update installation on the server.
    /// </summary>
    /// <param name="request">Install start request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resulting <see cref="UpdateStatusDto"/>, or <c>null</c> if none was returned.</returns>
    public async Task<UpdateStatusDto?> Updates_StartInstallAsync(UpdateStartRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/setup/update/install/start", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<UpdateStatusDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Resets a stale update lock on the server.
    /// </summary>
    /// <param name="request">Lock reset request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the lock was reset successfully.</returns>
    public async Task<bool> Updates_ResetLockAsync(UpdateLockResetRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/setup/update/lock/reset", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }
}
