using FinanceManager.Domain;

namespace FinanceManager.Application.Users;

public interface IUserAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterUserCommand command, CancellationToken ct);
    Task<Result<AuthResult>> LoginAsync(LoginCommand command, CancellationToken ct);
}
