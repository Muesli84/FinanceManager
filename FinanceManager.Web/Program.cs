using FinanceManager.Web;
using FinanceManager.Infrastructure;
using FinanceManager.Domain.Users;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// configure logging and services moved to extensions
builder.ConfigureLogging();
builder.RegisterAppServices();

var app = builder.Build();

// apply migrations and seeding
app.ApplyMigrationsAndSeed();

// configure localization and middleware
app.ConfigureLocalization();
app.ConfigureMiddleware();

app.Run();
