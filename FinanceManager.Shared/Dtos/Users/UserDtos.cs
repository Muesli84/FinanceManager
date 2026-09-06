namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// DTO indicating whether any users exist in the system.
/// </summary>
/// <param name="Any">The any.</param>
/// <returns>The result.</returns>
public sealed record AnyUsersResponse(bool Any);
