namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Response payload returned after successful authentication for anonymous callers.
/// Matches shape: { user, isAdmin, exp }.
/// </summary>
/// <param name="user">Authenticated user name.</param>
/// <param name="isAdmin">True when the user has administrative privileges.</param>
/// <param name="exp">Token expiry timestamp (UTC).</param>
/// <returns>The result.</returns>
public sealed record AuthOkResponse(string user, bool isAdmin, DateTime exp);
