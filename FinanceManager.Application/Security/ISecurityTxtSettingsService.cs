using FinanceManager.Shared.Dtos.Admin;

namespace FinanceManager.Application.Security;

/// <summary>
/// Abstraction for reading, updating and rendering security.txt settings.
/// </summary>
public interface ISecurityTxtSettingsService
{
    /// <summary>Returns the configured settings for admin editing.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<SecurityTxtSettingsDto> GetAsync(CancellationToken ct);
    /// <summary>Persists the configured settings.</summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(SecurityTxtSettingsUpdateRequest request, CancellationToken ct);
    /// <summary>Builds the public content for the given output format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<string?> BuildContentAsync(SecurityTxtFormat format, CancellationToken ct);
}
