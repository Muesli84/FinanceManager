namespace FinanceManager.Application.Users;

public interface IUserReadService
{
    Task<bool> HasAnyUsersAsync(CancellationToken ct);
}
