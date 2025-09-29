using FinanceManager.Application;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Setup;
using FinanceManager.Web.Components;
using FinanceManager.Web.Infrastructure;
using FinanceManager.Web.Services;
using FinanceManager.Web.Infrastructure.Auth;
using FinanceManager.Web.Infrastructure.Logging; // NEU
using Microsoft.AspNetCore.Authentication.JwtBearer; // NEU
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// .NET Logging: Console + File (aus appsettings)
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Services.Configure<FileLoggerOptions>(builder.Configuration.GetSection("FileLogging"));
builder.Logging.AddFile();

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
var supportedCultures = new[] { "de", "en" }.Select(c => new CultureInfo(c)).ToList();

if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]))
{
    throw new InvalidOperationException("Configuration 'Jwt:Key' missing. Set via user secrets: dotnet user-secrets set \"Jwt:Key\" \"<random>\"");
}

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

// Background task queue
builder.Services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();
builder.Services.AddSingleton<IBackgroundTaskExecutor, ClassificationTaskExecutor>();
builder.Services.AddSingleton<IBackgroundTaskExecutor, BookingTaskExecutor>();
builder.Services.AddSingleton<IBackgroundTaskExecutor, BackupRestoreTaskExecutor>();
builder.Services.AddHostedService<BackgroundTaskRunner>();

// NEW: Security prices
builder.Services.AddSingleton<IPriceProvider, AlphaVantagePriceProvider>();
builder.Services.AddHostedService<SecurityPriceWorker>();

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
builder.Services.AddScoped(sp =>
{
    var accessor = sp.GetRequiredService<IHttpClientFactory>();
    var client = accessor.CreateClient("Api");
    return client;
});

// JWT
var keyBytes = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // HTTP-only Umgebung
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
                    var cookie = ctx.Request.Cookies["fm_auth"];
                    if (!string.IsNullOrEmpty(cookie))
                    {
                        ctx.Token = cookie;
                    }
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("de"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<IpBlockMiddleware>(); // NEW: deny blocked IPs early

// EF Core Migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
        var schemaLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaPatcher");
        SchemaPatcher.EnsureUserImportSplitSettingsColumns(db, schemaLogger);
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
    {
        app.Logger.LogError(ex, "EF Core migrations failed – likely existing database created via EnsureCreated()");
        throw;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database migration failed");
        throw;
    }
}

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IAutoInitializationService>();
    initializer.Run();
}

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.Run();
