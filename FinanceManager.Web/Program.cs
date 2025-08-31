using FinanceManager.Web.Components;
using FinanceManager.Infrastructure;
using Serilog;
using FinanceManager.Application;
using FinanceManager.Web.Services;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Fail-Fast falls Jwt:Key fehlt
if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]))
{
    throw new InvalidOperationException("Configuration 'Jwt:Key' missing. Set via user secrets: dotnet user-secrets set \"Jwt:Key\" \"<random>\"");
}

// Serilog configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Controller Support
builder.Services.AddControllers();

// Infrastructure (DbContext, providers)
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Scoped HttpClient mit dynamischer Basisadresse (abhängig vom eingehenden Request)
builder.Services.AddScoped(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    var ctx = accessor.HttpContext;
    // Fallback auf http://localhost falls außerhalb eines HTTP-Kontexts (z.B. Hintergrundtasks)
    var baseUri = ctx != null ? $"{ctx.Request.Scheme}://{ctx.Request.Host.ToUriComponent()}/" : "http://localhost/";
    return new HttpClient { BaseAddress = new Uri($"{baseUri}") };
});

var app = builder.Build();

// DB initialisieren (einfaches EnsureCreated als Platzhalter – später Migrations verwenden)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database initialization failed");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Custom JWT Cookie -> ClaimsPrincipal Middleware
app.Use(async (ctx, next) =>
{
    var cookie = ctx.Request.Cookies["fm_auth"]; // JWT
    if (!string.IsNullOrEmpty(cookie))
    {
        try
        {
            var key = ctx.RequestServices.GetRequiredService<IConfiguration>()["Jwt:Key"]!;
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ClockSkew = TimeSpan.FromSeconds(10)
            };
            var principal = handler.ValidateToken(cookie, parameters, out _);
            ctx.User = principal; // ersetzt Default (anonym)
        }
        catch
        {
            // Ungültiges Token ignorieren (anonym weiter)
        }
    }
    await next();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map Controller Endpoints (API)
app.MapControllers();

app.Run();
