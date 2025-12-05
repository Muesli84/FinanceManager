using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManager.Tests.Auth
{
    public sealed class UserAdminServiceTests
    {
        private static (UserAdminService sut, AppDbContext db, Mock<IPasswordHashingService> hasher, Mock<UserManager<User>> userManagerMock) Create()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddLogging(); // Logger registrieren
            var sp = services.BuildServiceProvider();
            var db = sp.GetRequiredService<AppDbContext>();
            var hasher = new Mock<IPasswordHashingService>();
            hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"HASH::{p}");
            var logger = sp.GetRequiredService<ILogger<UserAdminService>>(); // Logger abrufen

            // create a minimal UserManager mock to satisfy constructor
            var store = new Mock<IUserStore<User>>();
            var userManagerMock = new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);
            userManagerMock.Setup(u => u.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(u => u.ResetAccessFailedCountAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

            // --- NEW: maintain role membership state inside the mock ---
            var adminUsers = new HashSet<Guid>();
            userManagerMock
                .Setup(um => um.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success)
                .Callback<User, string>((user, role) =>
                {
                    if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                        adminUsers.Add(user.Id);
                });

            userManagerMock
                .Setup(um => um.RemoveFromRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success)
                .Callback<User, string>((user, role) =>
                {
                    if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                        adminUsers.Remove(user.Id);
                });

            userManagerMock
                .Setup(um => um.IsInRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync((User user, string role) =>
                {
                    if (user == null || string.IsNullOrEmpty(role)) return false;
                    return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) && adminUsers.Contains(user.Id);
                });
            // --- END NEW ---

            var sut = new UserAdminService(db, userManagerMock.Object, hasher.Object, logger); // pass userManager
            return (sut, db, hasher, userManagerMock);
        }

        [Fact]
        public async Task CreateAsync_ShouldPersistUser()
        {
            var (sut, db, _, _) = Create();
            var dto = await sut.CreateAsync("alice", "Password1", true, CancellationToken.None);
            Assert.Equal("alice", dto.Username);
            Assert.True(dto.IsAdmin);
            Assert.Equal(1, db.Users.Count());
        }

        [Fact]
        public async Task CreateAsync_DuplicateUsername_Throws()
        {
            var (sut, db, _, _) = Create();
            db.Users.Add(new User("alice", "HASH::x", false));
            db.SaveChanges();
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync("alice", "pw", false, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateAsync_Rename_WhenUnique_Works()
        {
            var (sut, db, _, _) = Create();
            var u = new User("old", "HASH::x", false);
            db.Users.Add(u); db.SaveChanges();
            var updated = await sut.UpdateAsync(u.Id, "new", null, null, null, CancellationToken.None);
            Assert.Equal("new", updated!.Username);
        }

        [Fact]
        public async Task UpdateAsync_Rename_ToExisting_Throws()
        {
            var (sut, db, _, _) = Create();
            var a = new User("a", "HASH::x", false);
            var b = new User("b", "HASH::x", false);
            db.Users.AddRange(a, b); db.SaveChanges();
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateAsync(a.Id, "b", null, null, null, CancellationToken.None));
        }

        [Fact]
        public async Task ResetPasswordAsync_UpdatesHash()
        {
            var (sut, db, hasher, _) = Create();
            var u = new User("user", "HASH::old", false);
            db.Users.Add(u); db.SaveChanges();
            hasher.Setup(h => h.Hash("newpw")).Returns("HASH::newpw");
            var ok = await sut.ResetPasswordAsync(u.Id, "newpw", CancellationToken.None);
            Assert.True(ok);
            // Verify hash changed in tracked entity (reflection set) by reloading
            var re = db.Users.Single();
            Assert.Equal("HASH::newpw", re.PasswordHash);
        }

        [Fact]
        public async Task ResetPasswordAsync_Empty_Throws()
        {
            var (sut, db, _, _) = Create();
            var u = new User("user", "HASH::old", false);
            db.Users.Add(u); db.SaveChanges();
            await Assert.ThrowsAsync<ArgumentException>(() => sut.ResetPasswordAsync(u.Id, "", CancellationToken.None));
        }

        [Fact]
        public async Task UnlockAsync_ClearsIdentityLockout()
        {
            var (sut, db, _, userManagerMock) = Create();
            var u = new User("user", "HASH::x", false);
            // set Identity lockout end
            u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
            db.Users.Add(u); db.SaveChanges();

            var ok = await sut.UnlockAsync(u.Id, CancellationToken.None);
            Assert.True(ok);

            userManagerMock.Verify(um => um.SetLockoutEndDateAsync(It.Is<User>(x => x.Id == u.Id), null), Times.Once);
            userManagerMock.Verify(um => um.ResetAccessFailedCountAsync(It.Is<User>(x => x.Id == u.Id)), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_RemovesUser()
        {
            var (sut, db, _, _) = Create();
            var u = new User("user", "HASH::x", false);
            db.Users.Add(u); db.SaveChanges();
            var ok = await sut.DeleteAsync(u.Id, CancellationToken.None);
            Assert.True(ok);
            Assert.Equal(0, db.Users.Count());
        }

        [Fact]
        public async Task DeleteAsync_NonExisting_ReturnsFalse()
        {
            var (sut, db, _, _) = Create();
            var ok = await sut.DeleteAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.False(ok);
        }
    }
}
