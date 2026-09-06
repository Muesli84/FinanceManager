using FinanceManager.Domain.Users;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// Seeds users directly into the E2E test server's SQLite database - along with the companion data a user
/// needs to be usable (a "self" contact, and optional Identity "Admin" role membership) - bypassing the UI
/// registration flow so tests can arrange a known user/login state quickly and deterministically.
/// </summary>
public sealed class TestUserSeeder
{
    private const string AdminRoleName = "Admin";

    private readonly string _databasePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestUserSeeder"/> class.
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file used by the E2E test server.</param>
    public TestUserSeeder(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <summary>
    /// Ensures a user with the given username exists: creates it (with a hashed password, a self contact,
    /// and optional admin role membership) if it does not exist yet, or, if it already exists, brings its
    /// self contact and admin role membership in line with the requested state without recreating the user.
    /// </summary>
    /// <param name="username">The username to seed or look up.</param>
    /// <param name="password">The plaintext password to hash and store when a new user is created.</param>
    /// <param name="isAdmin">Whether the user should be granted the "Admin" Identity role.</param>
    /// <param name="timeZoneId">Optional IANA time zone identifier to persist for the user.</param>
    /// <returns>A task that resolves to the existing or newly created <see cref="User"/>.</returns>
    public async Task<User> EnsureUserAsync(string username, string password, bool isAdmin = false, string? timeZoneId = null)
    {
        using var db = CreateContext();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.UserName == username);
        if (existing != null)
        {
            existing.SetTimeZoneId(timeZoneId);
            await EnsureSelfContactInternalAsync(db, existing.Id, $"Self {username}");
            if (isAdmin)
            {
                await EnsureAdminRoleAssignedAsync(db, existing.Id);
            }

            await db.SaveChangesAsync();
            return existing;
        }

        var user = new User(username, new Pbkdf2IdentityPasswordHasher().Hash(password), isAdmin)
        {
            Id = Guid.NewGuid(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            NormalizedUserName = username.ToUpperInvariant(),
        };
        user.SetTimeZoneId(timeZoneId);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await EnsureSelfContactInternalAsync(db, user.Id, $"Self {username}");
        if (isAdmin)
        {
            await EnsureAdminRoleAssignedAsync(db, user.Id);
        }
        return user;
    }

    /// <summary>
    /// Grants the ASP.NET Identity "Admin" role to the given user. Login only includes the "Admin" claim when the
    /// user is a member of this Identity role (see <c>UserAuthService.LoginAsync</c>), so seeding
    /// <see cref="User.IsAdmin"/> alone is not sufficient to make admin-only UI (e.g. the setup update tab) visible.
    /// </summary>
    /// <param name="db">The database context used to read and persist the role assignment.</param>
    /// <param name="userId">Identifier of the user to grant the role to.</param>
    private static async Task EnsureAdminRoleAssignedAsync(AppDbContext db, Guid userId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == AdminRoleName);
        if (role == null)
        {
            role = new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = AdminRoleName,
                NormalizedName = AdminRoleName.ToUpperInvariant(),
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var alreadyAssigned = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == role.Id);
        if (!alreadyAssigned)
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Ensures the given user has a "self" contact - the contact record representing the user themselves,
    /// used e.g. as a counterparty for certain transactions - creating one with the given name if it does
    /// not already exist.
    /// </summary>
    /// <param name="userId">Identifier of the user to create the self contact for.</param>
    /// <param name="name">The display name to use if a self contact needs to be created.</param>
    public async Task EnsureSelfContactAsync(Guid userId, string name)
    {
        using var db = CreateContext();
        await EnsureSelfContactInternalAsync(db, userId, name);
    }

    /// <summary>
    /// Forces the given user's existing login sessions/cookies to become invalid, without changing their
    /// password, by regenerating their security stamp and concurrency stamp - useful for tests that need to
    /// verify behavior after a session has been invalidated out from under the user.
    /// </summary>
    /// <param name="username">The username whose security stamp should be invalidated.</param>
    public async Task InvalidateSecurityStampAsync(string username)
    {
        using var db = CreateContext();
        var user = await db.Users.SingleAsync(u => u.UserName == username);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
    }

    private static async Task EnsureSelfContactInternalAsync(AppDbContext db, Guid userId, string name)
    {
        var exists = await db.Contacts.AnyAsync(contact => contact.OwnerUserId == userId && contact.Type == ContactType.Self);
        if (exists)
        {
            return;
        }

        db.Contacts.Add(new Contact(userId, name, ContactType.Self, null));
        await db.SaveChangesAsync();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        return new AppDbContext(options);
    }
}
