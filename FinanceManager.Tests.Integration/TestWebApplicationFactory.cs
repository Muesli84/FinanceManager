using FinanceManager.Application; // BackgroundTaskRunner
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Web;
using FinanceManager.Web.Services; // SecurityPriceWorker
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting; // for IHostedService
using System.Data.Common;
using System.Diagnostics;

namespace FinanceManager.Tests.Integration;

/// <summary>
/// Custom <see cref="WebApplicationFactory{Program}"/> that wires <c>AppDbContext</c> to a fresh, isolated
/// SQLite in-memory database per factory instance, seeds a bootstrap admin user, disables background
/// hosted services that would otherwise interfere with deterministic tests, and serves help/static assets
/// from a private copy of the built web root so tests never mutate the real <c>wwwroot</c> folder.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Username of the admin account seeded into every test database, so tests can authenticate as an
    /// admin without needing to register the very first (auto-admin) user themselves.
    /// </summary>
    public const string BootstrapAdminUsername = "bootstrap.admin";

    /// <summary>
    /// Password of the admin account seeded into every test database. Paired with
    /// <see cref="BootstrapAdminUsername"/> to authenticate as an admin in tests.
    /// </summary>
    public const string BootstrapAdminPassword = "Bootstr4pAdmin!";

    /// <summary>
    /// Absolute path to the <c>FinanceManager.Web</c> project directory, used as the content root and to
    /// locate the built <c>wwwroot</c> folder that gets copied into each factory's isolated web root.
    /// </summary>
    /// <returns>The result.</returns>
    public static readonly string WebProjectRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "FinanceManager.Web"));
    private readonly string _isolatedWebRoot = Path.Combine(Path.GetTempPath(), $"fm-webroot-{Guid.NewGuid():N}");

    private DbConnection? _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestWebApplicationFactory"/> class, copying the built
    /// web root (help pages, static assets, integrity manifest) into a private, per-instance temp directory
    /// so tests can freely mutate or delete files without affecting other concurrently running factories.
    /// </summary>
    public TestWebApplicationFactory()
    {
        CopyDirectory(GetBuiltWebRoot(), _isolatedWebRoot);
    }

    // xUnit constructs many TestWebApplicationFactory instances concurrently (one per test class, run in
    // parallel collections by default). CI runs (fewer/slower cores than local dev machines) intermittently
    // hit transient "no such table" / SQLite errors during this startup phase, affecting whichever test
    // class's factory happened to be initializing at that moment - a different test each time, consistent
    // with a race in concurrent SQLite native-library-level database creation rather than a bug specific to
    // any one factory's own connection handling. Serializing schema creation and seeding across all factory
    // instances removes that concurrent-construction window; it only affects one-time test startup cost, not
    // the actual concurrent request handling the tests exercise.
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    /// <summary>
    /// When set, the factory will register a <see cref="TimeProvider"/> that returns this fixed UTC time.
    /// Set this property in tests before calling CreateClient() to force server-side "now".
    /// </summary>
    public DateTime? FixedUtcNow { get; set; }

    /// <summary>
    /// Absolute path to this factory's isolated copy of the web root, so tests can locate and mutate
    /// help/static asset files (or the integrity manifest) on disk without touching the shared build output.
    /// </summary>
    public string HelpWebRootPath => _isolatedWebRoot;

    /// <summary>
    /// Configures the test web host: points it at an isolated SQLite in-memory database and web root,
    /// disables background hosted services and file logging that would otherwise run unpredictably during
    /// tests, optionally installs a fixed <see cref="TimeProvider"/>, and seeds the bootstrap admin user.
    /// </summary>
    /// <param name="builder">The web host builder to configure for the in-memory test server.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        builder.UseEnvironment("Development");
        builder.UseContentRoot(WebProjectRoot);
        builder.UseWebRoot(_isolatedWebRoot);
        // Disable background hosted services for integration tests via configuration flags
        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["BackgroundTasks:Enabled"] = "false",
                ["Workers:SecurityPriceWorker:Enabled"] = "false",
                ["Updates:HostedServicesEnabled"] = "false",
                ["Updates:SourceType"] = "LocalFolder",
                ["Updates:LocalFolderPath"] = Path.Combine(Path.GetTempPath(), $"updates-source-{Guid.NewGuid():N}"),
                ["FileLogging:Enabled"] = "false"
            };
            cfg.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            // Remove specific hosted services so they do not start in tests
            var hostedToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            (d.ImplementationType == typeof(BackgroundTaskRunner)
                             || d.ImplementationType == typeof(SecurityPriceWorker)
                             || (d.ImplementationType?.Name == "MonthlyReminderScheduler")))
                .ToList();
            foreach (var d in hostedToRemove)
            {
                services.Remove(d);
            }

            // Remove existing AppDbContext registration
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Use a uniquely-named shared-cache in-memory database (not a shared SqliteConnection object):
            // a single SqliteConnection instance is not safe for concurrent use from multiple threads and
            // causes "SQLite Error 5: database is locked" when the background task runner and an HTTP
            // request thread access the database at the same time. Each AppDbContext instead opens its own
            // connection against the same named in-memory database, which SQLite handles safely for
            // concurrent access. The anchor connection below is kept open for the lifetime of the factory
            // so the named in-memory database isn't dropped between uses.
            //
            // The "file:...?mode=memory&cache=shared" URI form is used deliberately instead of the
            // "Data Source=name;Mode=Memory;Cache=Shared" keyword form: per SQLite's own documentation,
            // only the URI filename syntax is guaranteed to give multiple separate connections a genuinely
            // shared, name-addressable in-memory database. The keyword form was observed to work locally but
            // failed intermittently in CI with "no such table" errors, consistent with connections sometimes
            // not actually attaching to the same underlying in-memory database.
            // "Default Timeout" sets SQLite's busy-retry timeout (seconds): when two separate connections
            // briefly contend for the same shared-cache database, the later one waits instead of immediately
            // failing with "database is locked".
            var dbName = $"testdb_{Guid.NewGuid():N}";
            var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared;Default Timeout=30";

            // Serialize the whole create-database/migrate/seed critical section across all concurrently
            // constructing TestWebApplicationFactory instances (see InitializationGate for why).
            InitializationGate.Wait();
            try
            {
                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseSqlite(connectionString);
                });

                // If a fixed time was requested by the test, replace the application's TimeProvider
                // registration so server-side code observes the deterministic time.
                if (FixedUtcNow.HasValue)
                {
                    var dtDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));
                    if (dtDescriptor != null)
                    {
                        services.Remove(dtDescriptor);
                    }

                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedUtcNow.Value));
                }

                // Create and migrate the schema directly against the anchor connection object (not through the
                // connection-string-based DI registration used above). This removes any timing dependency on
                // SQLite's shared-cache database-name registration between opening the anchor connection and a
                // second, separately-opened connection resolved via DI reaching the same named database -
                // schema creation happens on the exact connection instance kept open for the factory's lifetime.
                var migrationOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
                using (var migrationDb = new AppDbContext(migrationOptions))
                {
                    migrationDb.Database.EnsureDeleted();
                    migrationDb.Database.Migrate();
                }

                // Seed a bootstrap admin user so that test registrations are never treated as the first user.
                // Without this, the first registered user in each test run would automatically receive Admin rights.
                // Resolved through the normal DI-registered (connection-string-based) AppDbContext: by this point
                // the schema is guaranteed present on the shared in-memory database via the anchor connection above.
                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                const string adminRole = "Admin";
                if (!roleManager.RoleExistsAsync(adminRole).GetAwaiter().GetResult())
                {
                    roleManager.CreateAsync(new IdentityRole<Guid> { Name = adminRole, NormalizedName = adminRole.ToUpperInvariant() }).GetAwaiter().GetResult();
                }
                var bootstrapAdmin = new User(BootstrapAdminUsername, isAdmin: true)
                {
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    LockoutEnabled = false,
                };
                var createBootstrapResult = userManager.CreateAsync(bootstrapAdmin, BootstrapAdminPassword).GetAwaiter().GetResult();
                if (!createBootstrapResult.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to seed bootstrap admin user: {string.Join("; ", createBootstrapResult.Errors.Select(e => e.Description))}");
                }

                var addToRoleResult = userManager.AddToRoleAsync(bootstrapAdmin, adminRole).GetAwaiter().GetResult();
                if (!addToRoleResult.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to assign bootstrap admin role: {string.Join("; ", addToRoleResult.Errors.Select(e => e.Description))}");
                }
            }
            finally
            {
                InitializationGate.Release();
            }
        });
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly long _timestamp;

        public FixedTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
            _timestamp = Stopwatch.GetTimestamp();
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;
    }

    /// <summary>
    /// Releases the anchor SQLite connection (which otherwise keeps the shared in-memory database alive
    /// for the factory's lifetime) and deletes this instance's isolated web root temp directory, so
    /// per-test resources do not leak across test runs.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources; <see langword="false"/> when called from a finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
            if (Directory.Exists(_isolatedWebRoot))
            {
                Directory.Delete(_isolatedWebRoot, recursive: true);
            }
        }
    }

    // Must match the configuration this test assembly itself was built with (Debug locally/in regular CI,
    // Release under release.yml's "Release gate - regular tests" step, which runs
    // `dotnet test --configuration Release`) - FinanceManager.Web is built as this project's own dependency
    // under that same configuration, so a hardcoded "Debug" here previously made every candidate below miss
    // under a Release test run, falling through to the source wwwroot folder - which never has
    // search-index.json/help-assets.sha256 (both gitignored as build-generated) - instead of the real build
    // output.
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private static string GetBuiltWebRoot()
    {
        var builtWebRoot = Path.Combine(WebProjectRoot, "bin", BuildConfiguration, "net10.0", "wwwroot");
        var integrationWebRoot = Path.Combine(WebProjectRoot, "bin", "FromFinanceManagerIntegrationTests", BuildConfiguration, "net10.0", "wwwroot");
        if (IsBuiltHelpWebRoot(integrationWebRoot))
        {
            return integrationWebRoot;
        }

        builtWebRoot = Path.Combine(WebProjectRoot, "bin", "FromFinanceManagerTests", BuildConfiguration, "net10.0", "wwwroot");
        if (IsBuiltHelpWebRoot(builtWebRoot))
        {
            return builtWebRoot;
        }

        builtWebRoot = Path.Combine(WebProjectRoot, "bin", BuildConfiguration, "net10.0", "wwwroot");
        if (IsBuiltHelpWebRoot(builtWebRoot))
        {
            return builtWebRoot;
        }

        builtWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return Directory.Exists(builtWebRoot)
            ? builtWebRoot
            : Path.Combine(WebProjectRoot, "wwwroot");
    }

    private static bool IsBuiltHelpWebRoot(string webRoot)
    {
        return File.Exists(Path.Combine(webRoot, "help", "help-assets.sha256"));
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }
}
