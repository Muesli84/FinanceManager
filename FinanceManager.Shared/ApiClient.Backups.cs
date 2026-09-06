using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Backups

    /// <summary>
    /// Lists backups owned by the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<BackupDto>> Backups_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/backups", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BackupDto>>(cancellationToken: ct) ?? Array.Empty<BackupDto>();
    }

    /// <summary>
    /// Creates a new backup entry for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BackupDto> Backups_CreateAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/setup/backups", content: null, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Uploads a backup file and returns the created backup metadata.
    /// </summary>
    /// <param name="fileStream">The file stream.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BackupDto> Backups_UploadAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        var resp = await _http.PostAsync("/api/setup/backups/upload", content, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Downloads the backup file stream or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<Stream?> Backups_DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/setup/backups/{id}/download", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var ms = new MemoryStream();
        await resp.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Starts applying the backup immediately and returns the restore status.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BackupRestoreStatusDto> Backups_StartApplyAsync(Guid id, BackupRestoreRequestDto request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/setup/backups/{id}/apply/start", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupRestoreStatusDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Gets status of the last or current backup restore operation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BackupRestoreStatusDto> Backups_GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/backups/restore/status", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupRestoreStatusDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Applies a backup immediately. Returns false when the backup id was not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Backups_ApplyAsync(Guid id, BackupRestoreRequestDto request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/setup/backups/{id}/apply", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Cancels an ongoing backup restore operation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Backups_CancelAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/setup/backups/restore/cancel", content: null, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Deletes a backup entry. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Backups_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/setup/backups/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Backups
}
