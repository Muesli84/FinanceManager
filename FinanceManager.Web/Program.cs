using FinanceManager.Application;
using FinanceManager.Infrastructure;
using FinanceManager.Web.Components;
using FinanceManager.Web.Infrastructure;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer; // NEU
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
var supportedCultures = new[] { "de", "en" }.Select(c => new CultureInfo(c)).ToList();

if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]))
{
    throw new InvalidOperationException("Configuration 'Jwt:Key' missing. Set via user secrets: dotnet user-secrets set \"Jwt:Key\" \"<random>\"");
}

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024; // z.B. 20 MB
});

// Named HttpClient (bleibt)
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

// JWT Authentication (Header Bearer + Cookie fm_auth)
var keyBytes = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;
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
        // Zusätzliche Quelle: Cookie falls kein Authorization Header
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

// EF Core Migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
    {
        Log.Error(ex, "EF Core migrations failed – likely existing database created via EnsureCreated()");
        throw;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database migration failed");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Authentication / Authorization einschleusen
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.Run();
