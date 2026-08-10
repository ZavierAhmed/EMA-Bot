using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<TradingDefaultsOptions>(builder.Configuration.GetSection(TradingDefaultsOptions.SectionName));

// A real connection string is supplied through user-secrets or environment variables.
// The credential-free fallback keeps the API startable so /api/health can report an unavailable database.
var connectionString = builder.Configuration.GetConnectionString("EmaBotDatabase")
    ?? "Server=localhost;Database=emabot;";

builder.Services.AddDbContext<EmaBotDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 4, 0)),
        mySqlOptions =>
        {
            mySqlOptions.EnableRetryOnFailure();
            mySqlOptions.TranslateParameterizedCollectionsToConstants();
        }));

builder.Services
    .AddIdentity<EmaUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<EmaBotDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "emabot.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "emabot.csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TradingSettingsService>();
builder.Services.AddSingleton<BacktestEngine>();
builder.Services.AddScoped<BacktestService>();
builder.Services.AddSingleton<EmaSignalEngine>();
builder.Services.AddHttpClient<IBinanceFuturesMarketDataClient, BinanceFuturesMarketDataClient>(client =>
{
    client.BaseAddress = new Uri("https://fapi.binance.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EMA-Bot/1.0");
});
builder.Services.AddScoped<IBinanceHistoricalCandleService, BinanceHistoricalCandleService>();
builder.Services.AddSingleton<IBinanceFuturesStreamClient, BinanceFuturesStreamClient>();
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<PaperTradingCoordinator>();
builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PaperTradingCoordinator>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." });
        });
    });
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
