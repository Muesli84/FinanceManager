using FinanceManager.Application;
using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Notifications;
using FinanceManager.Infrastructure.Setup;
using FinanceManager.Web.Components;
using FinanceManager.Web.Infrastructure;
using FinanceManager.Web.Infrastructure.Attachments;
using FinanceManager.Web.Infrastructure.Auth;
using FinanceManager.Web.Infrastructure.Logging;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Text;
using FinanceManager.Shared; // register ApiClient

namespace FinanceManager.Web
{
    public static class ProgramExtensions
    {
        public static void ConfigureLogging(this WebApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddConsole();
            builder.Services.Configure<FileLoggerOptions>(builder.Configuration.GetSection("FileLogging"));
            builder.Logging.AddFile();
        }

        public static void RegisterAppServices(this WebApplicationBuilder builder)
        {
            // Localization
            builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddControllers();
            builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default"));
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L; // 1 GB
            });

            // Attachment upload validation options
            builder.Services.Configure<AttachmentUploadOptions>(builder.Configuration.GetSection("Attachments"));

            // Background task queue
            builder.Services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, ClassificationTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, BookingTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, BackupRestoreTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, SecurityPricesBackfillExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, RebuildAggregatesTaskExecutor>();
            builder.Services.AddHostedService<BackgroundTaskRunner>();

            // Holidays
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<InMemoryHolidayProvider>();
            builder.Services.AddSingleton<NagerDateHolidayProvider>();
            builder.Services.AddSingleton<IHolidaySubdivisionService, NagerDateSubdivisionService>();
            builder.Services.AddSingleton<IHolidayProviderResolver, HolidayProviderResolver>();

            // Notifications
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddScoped<INotificationWriter, NotificationWriter>();

            builder.Services.AddScoped<IPostingsQueryService, PostingsQueryService>();

            // Monthly reminder scheduler
            builder.Services.AddScoped<MonthlyReminderJob>();
            builder.Services.AddHostedService<MonthlyReminderScheduler>();

            // HttpClient
            builder.Services.AddTransient<AuthenticatedHttpClientHandler>();
            builder.Services.AddSingleton<IAuthTokenProvider, JwtCookieAuthTokenProvider>();
            builder.Services.AddHttpClient("Api", (sp, client) =>
            {
                var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                var ctx = accessor.HttpContext;
                var baseUri = ctx != null
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host.ToUriComponent()}/"
                    : builder.Configuration["Api:BaseAddress"] ?? "https://localhost:5001/";
                client.BaseAddress = new Uri(baseUri);
            }).AddHttpMessageHandler<AuthenticatedHttpClientHandler>();
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));
            builder.Services.AddScoped<IApiClient>(sp => new ApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api")));

            // AlphaVantage
            builder.Services.AddHttpClient("AlphaVantage", client =>
            {
                client.BaseAddress = new Uri("https://www.alphavantage.co/");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FinanceManager/1.0 (+https://github.com/Muesli84/FinanceManager)");
            });
            builder.Services.AddScoped<IAlphaVantageKeyResolver, AlphaVantageKeyResolver>();
            builder.Services.AddScoped<IPriceProvider, AlphaVantagePriceProvider>();
            builder.Services.AddHostedService<SecurityPriceWorker>();
            builder.Services.Configure<AlphaVantageQuotaOptions>(builder.Configuration.GetSection("AlphaVantage:Quota"));

            // JWT + Identity
            var keyBytes = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.SaveToken = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                        ClockSkew = TimeSpan.FromSeconds(10)
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = ctx =>
                        {
                            if (string.IsNullOrEmpty(ctx.Token))
                            {
                                var cookie = ctx.Request.Cookies["FinanceManager.Auth"];
                                if (!string.IsNullOrEmpty(cookie))
                                {
                                    ctx.Token = cookie;
                                }
                            }
                            return Task.CompletedTask;
                        }
                    };
                });

            var identitySection = builder.Configuration.GetSection("Identity");
            var lockoutSection = identitySection.GetSection("Lockout");
            var passwordSection = identitySection.GetSection("Password");

            builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
            {
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockoutSection.GetValue<int>("DefaultLockoutMinutes", 5));
                options.Lockout.MaxFailedAccessAttempts = lockoutSection.GetValue<int>("MaxFailedAccessAttempts", 3);
                options.Lockout.AllowedForNewUsers = lockoutSection.GetValue<bool>("AllowedForNewUsers", true);

                options.Password.RequireDigit = passwordSection.GetValue<bool>("RequireDigit", true);
                options.Password.RequiredLength = passwordSection.GetValue<int>("RequiredLength", 8);
                options.Password.RequireNonAlphanumeric = passwordSection.GetValue<bool>("RequireNonAlphanumeric", false);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

            // Use custom PBKDF2 password hasher implementation from Infrastructure so UserManager hashes passwords consistently
            builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, FinanceManager.Infrastructure.Auth.Pbkdf2IdentityPasswordHasher>();

            builder.Services.ConfigureApplicationCookie(options =>
            {
                // Unique cookie name for this app (prevents collision with other apps on same domain)
                options.Cookie.Name = "FinanceManager.Identity";
                options.Cookie.Path = "/"; // keep app-wide; could be app-specific path if you host under same domain/path
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // or Always in prod

                options.LoginPath = "/Login";
                options.AccessDeniedPath = "/AccessDenied";
                options.SlidingExpiration = true;

                var jwtLifetimeMinutes = builder.Configuration.GetValue<int?>("Jwt:LifetimeMinutes") ?? 30;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(jwtLifetimeMinutes);
            });

            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.Name = "FinanceManager.Antiforgery";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

            builder.Services.AddAuthorization();
        }

        public static void ConfigureLocalization(this WebApplication app)
        {
            var supportedCultures = new[] { "de", "en" }.Select(c => new CultureInfo(c)).ToList();
            var locOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("de"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };
            locOptions.RequestCultureProviders.Insert(0, new UserPreferenceRequestCultureProvider());
            app.UseRequestLocalization(locOptions);
        }

        public static void ConfigureMiddleware(this WebApplication app)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<IpBlockMiddleware>();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
            }
            else
            {
                app.UseHttpsRedirection();
            }

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseMiddleware<JwtRefreshMiddleware>();

            app.MapStaticAssets();
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            app.MapControllers();
        }

        public static void ApplyMigrationsAndSeed(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                db.Database.Migrate();
                var schemaLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaPatcher");
                // Run runtime patcher to trigger background work that cannot run in migrations (e.g. rebuilding aggregates)
                try
                {
                    SchemaPatcher.RunPostMigrationPatches(scope.ServiceProvider, db, schemaLogger);
                }
                catch (Exception ex)
                {
                    schemaLogger.LogError(ex, "Post-migration patches failed");
                }
                EnsureAdminRole(app, scope, db);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                app.Logger.LogError(ex, "EF Core migrations failed ? likely existing database created via EnsureCreated()");
                throw;
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Database migration failed");
                throw;
            }

            // Run auto-initializer if present
            using var scope2 = app.Services.CreateScope();
            var initializer = scope2.ServiceProvider.GetService<IAutoInitializationService>();
            initializer?.Run();
        }

        private static void EnsureAdminRole(WebApplication app, IServiceScope scope, AppDbContext db)
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var roleExists = roleManager.RoleExistsAsync("Admin").GetAwaiter().GetResult();
            if (!roleExists)
            {
                var r = new IdentityRole<Guid> { Name = "Admin", NormalizedName = "ADMIN" };
                roleManager.CreateAsync(r).GetAwaiter().GetResult();
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

            // Ensure users that are marked IsAdmin in domain are members of the Admin role
            var domainAdmins = db.Users.AsNoTracking().Where(u => u.IsAdmin).ToList();
            foreach (var du in domainAdmins)
            {
                try
                {
                    var identityUser = userManager.FindByIdAsync(du.Id.ToString()).GetAwaiter().GetResult();
                    if (identityUser != null && !userManager.IsInRoleAsync(identityUser, "Admin").GetAwaiter().GetResult())
                    {
                        var addRes = userManager.AddToRoleAsync(identityUser, "Admin").GetAwaiter().GetResult();
                        if (!addRes.Succeeded)
                        {
                            app.Logger.LogWarning("Failed to add user {UserId} to Admin role: {Errors}", du.Id, string.Join(", ", addRes.Errors.Select(e => e.Description)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to ensure user {UserId} is in Admin role", du.Id);
                }
            }

            // Ensure users in Admin role are marked as IsAdmin in domain
            var usersInRole = userManager.GetUsersInRoleAsync("Admin").GetAwaiter().GetResult();
            var domainChanged = false;
            foreach (var iu in usersInRole)
            {
                try
                {
                    var domainUser = db.Users.FirstOrDefault(u => u.Id == iu.Id);
                    if (domainUser != null && !domainUser.IsAdmin)
                    {
                        domainUser.SetAdmin(true);
                        domainChanged = true;
                    }
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to sync role->domain IsAdmin for user {UserId}", iu.Id);
                }
            }

            if (domainChanged)
            {
                try
                {
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Failed to persist IsAdmin changes to domain users");
                }
            }
        }
    }
}
