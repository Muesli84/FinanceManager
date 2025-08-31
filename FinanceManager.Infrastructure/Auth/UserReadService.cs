using FinanceManager.Application.Users;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserReadService : IUserReadService
{
    private readonly AppDbContext _db;
    public UserReadService(AppDbContext db) => _db = db;
    public Task<bool> HasAnyUsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().AnyAsync(ct);
}
