using FinanceManager.Shared.Dtos.Admin;
using System.Net.Http.Json;

namespace FinanceManager.Shared;

/// <summary>
/// Security.txt admin API extensions for <see cref="ApiClient"/>.
/// </summary>
public partial class ApiClient
{
    /// <summary>Reads the current security.txt settings.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<SecurityTxtSettingsDto?> GetSecurityTxtSettingsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/admin/security-txt", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SecurityTxtSettingsDto>(cancellationToken: ct);
    }

    /// <summary>Updates the current security.txt settings.</summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateSecurityTxtSettingsAsync(SecurityTxtSettingsUpdateRequest request, CancellationToken ct = default)
    {
        var response = await _http.PutAsync("api/admin/security-txt", JsonContent.Create(request), ct);
        response.EnsureSuccessStatusCode();
    }
}
