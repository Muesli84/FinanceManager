using FinanceManager.Application;
using FinanceManager.Application.Notifications;
using FinanceManager.Application.Securities.ReturnAnalysis;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Infrastructure.Backups;
using FinanceManager.Infrastructure.Notifications;
using FinanceManager.Infrastructure.Setup;
using FinanceManager.Infrastructure.Security;
using FinanceManager.Application.Security;
using FinanceManager.Shared; // register ApiClient
using FinanceManager.Web.Components;
using FinanceManager.Web.Infrastructure;
using FinanceManager.Web.Infrastructure.Attachments;
using FinanceManager.Web.Infrastructure.Auth;
using FinanceManager.Web.Infrastructure.Logging;
using FinanceManager.Web.Services;
using FinanceManager.Web.Services.Help;
using FinanceManager.Web.Services.Updates;
using msTools.Web.Blazor;
using msTools.Updater;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;

namespace FinanceManager.Web
{
    /// <summary>
    /// Extension methods used to configure the web application builder and the resulting WebApplication.
    /// Encapsulates logging, DI registrations, localization and middleware setup used by the web project.
    /// </summary>
    public static class ProgramExtensions
    {
        /// <summary>
        /// Configures logging providers for the application. Adds console logging and optionally a file logger
        /// according to configuration section "FileLogging".
        /// </summary>
        /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
        /// <remarks>
        /// Reads configuration keys under "FileLogging" to determine whether file logging should be enabled
        /// and to bind <see cref="FileLoggerOptions"/> when enabled.
        /// </remarks>
        public static void ConfigureLogging(this WebApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddConsole();

            // File logging optional via config flag
            var fileLoggingEnabled = builder.Configuration.GetValue<bool?>("FileLogging:Enabled") ?? true;
            if (fileLoggingEnabled)
            {
                builder.Services.Configure<FileLoggerOptions>(builder.Configuration.GetSection("FileLogging"));
                builder.Logging.AddFile();
            }
        }

        /// <summary>
        /// Registers application services, middleware dependencies and framework services required by the Blazor web project.
        /// This method wires up localization, Razor Components, HTTP clients, background workers, authentication and various app services.
        /// </summary>
        /// <param name="builder">The <see cref="WebApplicationBuilder"/> used to register services.</param>
        public static void RegisterAppServices(this WebApplicationBuilder builder)
        {
            // Localization
            builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
            builder.Services.AddSingleton<IStringLocalizer<Pages>, PagesStringLocalizer>();
            builder.Services.AddScoped<IReturnAnalysisLocalizer, ReturnAnalysisLocalizer>();

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddControllers();
            builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default"));
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            var attachmentMaxSizeBytes = AttachmentUploadOptions.NormalizeMaxSizeBytes(
                builder.Configuration.GetValue<long?>("Attachments:MaxSizeBytes")
                    ?? AttachmentUploadOptions.DefaultMaxSizeBytes);
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = attachmentMaxSizeBytes;
            });

            // Attachment upload validation options
            builder.Services.Configure<AttachmentUploadOptions>(builder.Configuration.GetSection("Attachments"));
            builder.Services.AddSingleton<IAttachmentContentPolicy, AttachmentContentPolicy>();
            builder.Services.Configure<BackupSecurityOptions>(builder.Configuration.GetSection(BackupSecurityOptions.SectionName));
            builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection(UpdateOptions.SectionName));
            var dataProtectionBuilder = builder.Services.AddDataProtection();
            var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
            {
                dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
            }

            // Background task queue
            builder.Services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, ClassificationTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, BookingTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, BackupRestoreTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, SecurityPricesBackfillExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, RebuildAggregatesTaskExecutor>();
            builder.Services.AddSingleton<IBackgroundTaskExecutor, ReportCacheRefreshTaskExecutor>();
            // Conditionally enable BackgroundTaskRunner via config flag
            var enableTaskRunner = builder.Configuration.GetValue<bool?>("BackgroundTasks:Enabled") ?? true;
            if (enableTaskRunner)
            {
                builder.Services.AddHostedService<BackgroundTaskRunner>();
            }

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
            builder.Services.AddScoped<Application.Postings.IPostingsQueryService>(sp => sp.GetRequiredService<IPostingsQueryService>());
            builder.Services.AddScoped<Application.Budget.IBudgetReportExportService, BudgetReportExportService>();
            builder.Services.AddLoadingBar(options =>
            {
                options.Colors = new[]
                {
                    "#4dabf7",
                    "#51cf66",
                    "#ffd43b",
                    "#ff6b6b",
                    "#b197fc",
                    "#20c997",
                    "#ff922b"
                };
                options.Height = "3px";
                options.Top = "0";
                options.MobileTop = "54px";
                options.MobileBreakpoint = "900px";
                options.ZIndex = 1200;
            });
            builder.Services.AddSingleton<IHelpContentRenderer, HelpContentRenderer>();
            builder.Services.AddSingleton<IHelpAssetIntegrityValidator, HelpAssetIntegrityValidator>();

            // Monthly reminder scheduler
            builder.Services.AddScoped<MonthlyReminderJob>();
            builder.Services.AddHostedService<MonthlyReminderScheduler>();

            // HttpClient
            builder.Services.AddTransient<AuthenticatedHttpClientHandler>();
            builder.Services.AddScoped<IAuthTokenProvider, JwtCookieAuthTokenProvider>();
            builder.Services.AddScoped<ISecurityTxtSettingsService, SecurityTxtSettingsService>();
            builder.Services.AddHttpClient("Api", (sp, client) =>
            {
                var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                var ctx = accessor.HttpContext;
                var configuredBaseUri = builder.Configuration["Api:BaseAddress"];
                var baseUri = !string.IsNullOrWhiteSpace(configuredBaseUri)
                    ? configuredBaseUri
                    : ctx != null
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host.ToUriComponent()}/"
                    : "https://localhost:5001/";
                client.BaseAddress = new Uri(baseUri);
            }).AddHttpMessageHandler<AuthenticatedHttpClientHandler>();
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));
            builder.Services.AddScoped<IApiClient>(sp => new ApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api")));
            builder.Services.AddScoped<IKpiLocalStorageCache, KpiLocalStorageCache>();

            // Self-update services: the auto-update subsystem is provided by the external msTools.Updater release
            // and activated through the single UseAutoUpdate() entry point. The Web project only keeps the adapter
            // that maps the library onto the existing IUpdateOrchestrator contract and DTOs, plus the
            // FinanceManager-specific settings persistence and installed-version display.
            builder.Services.AddSingleton(TimeProvider.System);
            var updateOptions = builder.Configuration.GetSection(UpdateOptions.SectionName).Get<UpdateOptions>() ?? new UpdateOptions();
            builder.UseAutoUpdate(cfg =>
            {
                cfg.SetInitialConfiguration(updateOptions);
            });
            builder.Services.AddScoped<IUpdateOrchestrator, UpdateOrchestratorAdapter>();
            builder.Services.AddSingleton<IUpdateSettingsStore, UpdateSettingsStore>();
            builder.Services.AddSingleton<IInstalledReleaseMetadataProvider, InstalledReleaseMetadataProvider>();
            builder.Services.AddSingleton<UpdateStatusMapper>();
            builder.Services.AddSingleton<IUpdateServiceCatalog, DefaultUpdateServiceCatalog>();

            // AlphaVantage
            builder.Services.AddHttpClient("AlphaVantage", client =>
            {
                client.BaseAddress = new Uri("https://www.alphavantage.co/");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FinanceManager/1.0 (+https://github.com/Muesli84/FinanceManager)");
            });
            builder.Services.AddScoped<IAlphaVantageSecretProtector, DataProtectionAlphaVantageSecretProtector>();
            builder.Services.AddScoped<IAlphaVantageKeyResolver, AlphaVantageKeyResolver>();
            builder.Services.AddScoped<IPriceProvider, AlphaVantagePriceProvider>();
            // Conditionally enable SecurityPriceWorker via config flag
            var enableSecurityPriceWorker = builder.Configuration.GetValue<bool?>("Workers:SecurityPriceWorker:Enabled") ?? true;
            if (enableSecurityPriceWorker)
            {
                builder.Services.AddHostedService<SecurityPriceWorker>();
            }
            builder.Services.Configure<AlphaVantageQuotaOptions>(builder.Configuration.GetSection("AlphaVantage:Quota"));

            // JWT + Identity
            builder.Services
                .AddOptions<JwtOptions>()
                .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<JwtOptions>>(new JwtOptionsValidator(builder.Environment));
            builder.Services.AddSingleton<JwtTokenValidationParametersFactory>();
            builder.Services.AddScoped<IJwtRefreshService, JwtRefreshService>();

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.SaveToken = false;
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
                        },
                        OnTokenValidated = async ctx =>
                        {
                            var userIdValue = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                                ?? ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                            var tokenStamp = ctx.Principal?.FindFirstValue(JwtRefreshService.SecurityStampClaimType);
                            if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(tokenStamp))
                            {
                                ctx.Fail("Invalid token user state");
                                return;
                            }

                            var userManager = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
                            var user = await userManager.FindByIdAsync(userId.ToString());
                            if (user is null || !user.Active)
                            {
                                ctx.Fail("Invalid token user state");
                                return;
                            }

                            var currentStamp = await userManager.GetSecurityStampAsync(user);
                            if (!string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal))
                            {
                                ctx.Fail("Invalid token user state");
                                return;
                            }

                            var tokenHasAdminRole = ctx.Principal?.IsInRole("Admin") == true;
                            var currentHasAdminRole = await userManager.IsInRoleAsync(user, "Admin");
                            if (tokenHasAdminRole != currentHasAdminRole)
                            {
                                ctx.Fail("Invalid token user state");
                            }
                        }
                    };
                });
            builder.Services
                .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<JwtTokenValidationParametersFactory>((options, factory) =>
                {
                    options.TokenValidationParameters = factory.Create();
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

            });
            builder.Services
                .AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
                .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(jwtOptions.Value.LifetimeMinutes);
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

        private static void SetInitialConfiguration(this AutoUpdateBuilder cfg, UpdateOptions updateOptions)
        {
            cfg.BindConfiguration(UpdateOptions.SectionName);
            cfg.WithUpdateUnitName("FinanceManagerUpdate");
            if (!string.IsNullOrWhiteSpace(updateOptions.WorkingDirectory))
            {
                cfg.WithDownloadPath(updateOptions.WorkingDirectory);
            }

            cfg.WithSourceCheck(
                AutoUpdateOptionsMapper.DailySourceCheckIntervalMinutes,
                AutoUpdateOptionsMapper.BuildSourceCheckTimeRanges(updateOptions.SourceCheckStartTime, updateOptions.SourceCheckEndTime));

            if (string.Equals(updateOptions.SourceType, "LocalFolder", StringComparison.OrdinalIgnoreCase))
            {
                cfg.UseLocalFolderSource(
                    string.IsNullOrWhiteSpace(updateOptions.LocalFolderPath)
                        ? Path.Combine(updateOptions.WorkingDirectory, "source")
                        : updateOptions.LocalFolderPath,
                    updateOptions.ManifestAssetName);
            }
            else
            {
                cfg.UseGithubSource(updateOptions.RepositoryOwner, updateOptions.RepositoryName, updateOptions.ManifestAssetName);
            }
        }

        /// <summary>
        /// Builds the request localization options with supported cultures and the custom
        /// <see cref="UserPreferenceRequestCultureProvider"/>.
        /// <para>
        /// <b>Important:</b> the returned options must be applied via <c>app.UseRequestLocalization</c>
        /// <em>after</em> <c>app.UseAuthentication()</c> so that <see cref="HttpContext.User"/> is already
        /// populated when <see cref="UserPreferenceRequestCultureProvider"/> reads the JWT claims.
        /// </para>
        /// </summary>
        /// <param name="_">The  .</param>
        /// <returns>Configured <see cref="RequestLocalizationOptions"/>.</returns>
        public static RequestLocalizationOptions BuildLocalizationOptions(this WebApplication _)
        {
            var supportedCultures = HelpLanguages.Supported.Select(c => new CultureInfo(c)).ToList();
            var locOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(HelpLanguages.DefaultLanguage),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };
            locOptions.RequestCultureProviders.Insert(0, new UserPreferenceRequestCultureProvider());
            return locOptions;
        }

        /// <summary>
        /// Configures middleware components and routing for the application including authentication, authorization,
        /// static files, antiforgery and custom middleware such as IP blocking and JWT refresh.
        /// </summary>
        /// <param name="app">The <see cref="WebApplication"/> instance to configure.</param>
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
                // Surfaces the real exception (stack trace in the response body) instead of a bare,
                // bodyless 500, which otherwise made unhandled exceptions during local development and
                // integration tests (both run with Environment=Development) impossible to diagnose.
                app.UseDeveloperExceptionPage();

                if (!app.Configuration.GetValue<bool>("E2E:DisableHttpsRedirection"))
                {
                    app.UseHttpsRedirection();
                }
            }

            if (app.Configuration.GetValue<bool>("E2E:AccountStatisticsFaultInjectionEnabled"))
            {
                var accountStatisticsFaultFile = app.Configuration["E2E:AccountStatisticsFaultFile"];
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.Equals("/api/accounts/statistics", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(accountStatisticsFaultFile)
                        && File.Exists(accountStatisticsFaultFile))
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new { error = "E2E_AccountStatisticsFault", message = "Injected account statistics failure." });
                        return;
                    }

                    await next();
                });
            }

            app.Use(async (context, next) =>
            {
                if (HelpSecurityPolicy.IsHelpPath(context.Request.Path))
                {
                    context.Response.Headers.ContentSecurityPolicy = HelpSecurityPolicy.ContentSecurityPolicy;
                }

                await next();
            });

            app.Use(async (context, next) =>
            {
                if (!HelpSecurityPolicy.IsStaticHelpAssetPath(context.Request.Path))
                {
                    await next();
                    return;
                }

                var relativePath = context.Request.Path.Value?.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = relativePath is null
                    ? null
                    : Path.Combine(app.Environment.WebRootPath, relativePath);

                var validator = context.RequestServices.GetRequiredService<IHelpAssetIntegrityValidator>();
                if (fullPath is null || !validator.IsTrustedHelpFile(fullPath))
                {
                    app.Logger.LogWarning("Blocked untrusted static help asset: {Path}", context.Request.Path.Value);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next();
            });

            // Serve ALL static files including help HTML pages.
            // /.well-known/ requests must not be handled as static files so that
            // the SecurityTxtController endpoints take precedence.
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    if (ctx.Context.Request.Path.StartsWithSegments("/.well-known"))
                    {
                        ctx.Context.Response.StatusCode = StatusCodes.Status404NotFound;
                        ctx.Context.Response.ContentLength = 0;
                        ctx.Context.Response.Body = Stream.Null;
                    }
                }
            });
            app.UseAntiforgery();

            app.UseAuthentication();
            app.UseAuthorization();

            // Localization must run AFTER authentication so that HttpContext.User is populated
            // when UserPreferenceRequestCultureProvider reads the pref_lang JWT claim.
            app.UseRequestLocalization(app.BuildLocalizationOptions());

            app.UseMiddleware<JwtRefreshMiddleware>();

            // Map static assets for CSS/JS bundling
            app.MapStaticAssets();

            // Blazor routing comes last (catches everything else)
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            app.MapControllers();
        }

        /// <summary>
        /// Applies pending EF Core migrations, runs any post-migration patches and ensures the Admin role and domain admin mappings exist.
        /// </summary>
        /// <param name="app">The <see cref="WebApplication"/> instance used to create scopes and obtain services.</param>
        /// <remarks>
        /// This method will migrate the database and may throw exceptions on failure. It also attempts to run
        /// a runtime SchemaPatcher and to ensure that domain users with IsAdmin are members of the Admin identity role.
        /// </remarks>
        /// <exception cref="Microsoft.Data.Sqlite.SqliteException">May be thrown when running migrations against a SQLite database with an unexpected schema.</exception>
        /// <exception cref="Exception">Any exception raised during migration will be rethrown after logging.</exception>
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

        /// <summary>
        /// Applies the persisted update settings (from <see cref="IUpdateSettingsStore"/>) onto the auto-update
        /// library's runtime <c>AutoUpdateOptions</c> singleton before the host starts.
        /// </summary>
        /// <param name="app">The <see cref="WebApplication"/> instance used to resolve services and for logging.</param>
        /// <remarks>
        /// <see cref="AutoUpdateHostBuilderExtensions.UseAutoUpdate"/> only seeds <c>AutoUpdateOptions</c> from
        /// <c>appsettings*.json</c>; without this step, settings previously saved through the setup UI would only
        /// take effect once the admin saves them again after each restart (they were applied in-memory by
        /// <see cref="IUpdateSettingsStore.ApplyToOptions"/> only from <c>UpdateOrchestratorAdapter.SaveSettingsAsync</c>/
        /// <c>ScheduleAsync</c>), while the auto-update hosted services would keep running against the
        /// configuration-only defaults after every restart. Must run before <c>app.Run()</c>/<c>app.RunAsync()</c>
        /// starts <c>AutoUpdateCheckerService</c>/<c>AutoUpdateSchedulerService</c>, which read <c>AutoUpdateOptions</c>
        /// as soon as they start. Failures are logged and swallowed so that a corrupt settings file cannot prevent
        /// the application from starting; the appsettings-configured defaults remain in effect in that case.
        /// </remarks>
        public static void ApplyPersistedUpdateSettings(this WebApplication app)
        {
            try
            {
                var settingsStore = app.Services.GetRequiredService<IUpdateSettingsStore>();
                var settings = settingsStore.GetAsync().GetAwaiter().GetResult();
                settingsStore.ApplyToOptions(settings);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Failed to apply persisted update settings at startup; using appsettings-configured defaults instead.");
            }
        }

        /// <summary>
        /// Ensures that the Admin identity role exists and synchronizes role membership with domain users marked as IsAdmin.
        /// This is an internal helper used by <see cref="ApplyMigrationsAndSeed(WebApplication)"/>.
        /// </summary>
        /// <param name="app">The application instance used for logging.</param>
        /// <param name="scope">A service scope used to resolve scoped services such as RoleManager and UserManager.</param>
        /// <param name="db">The application database context.</param>
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
