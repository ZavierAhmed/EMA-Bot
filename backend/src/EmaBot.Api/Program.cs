using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Configuration;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<TradingDefaultsOptions>(builder.Configuration.GetSection(TradingDefaultsOptions.SectionName));
builder.Services.AddOptions<Mt5BridgeOptions>().Bind(builder.Configuration.GetSection(Mt5BridgeOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<Mt5BridgeOptions>, Mt5BridgeOptionsValidator>();
builder.Services.AddOptions<Mt5MarketDataOptions>().Bind(builder.Configuration.GetSection(Mt5MarketDataOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<Mt5MarketDataOptions>, Mt5MarketDataOptionsValidator>();
builder.Services.AddOptions<Mt5ExecutionBridgeOptions>().Bind(builder.Configuration.GetSection(Mt5ExecutionBridgeOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<Mt5ExecutionBridgeOptions>, Mt5ExecutionBridgeOptionsValidator>();
builder.Services.AddOptions<DemoExecutionOptions>().Bind(builder.Configuration.GetSection(DemoExecutionOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DemoExecutionOptions>, DemoExecutionOptionsValidator>();
builder.Services.AddOptions<DemoStrategyAutomationOptions>().Bind(builder.Configuration.GetSection(DemoStrategyAutomationOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DemoStrategyAutomationOptions>, DemoStrategyAutomationOptionsValidator>();

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
builder.Services.AddOptions<BacktestRequestTimeoutOptions>().Bind(builder.Configuration.GetSection(BacktestRequestTimeoutOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<BacktestRequestTimeoutOptions>, BacktestRequestTimeoutOptionsValidator>();
builder.Services.AddScoped<TradingSettingsService>();
builder.Services.AddSingleton<BacktestEngine>();
builder.Services.AddScoped<BacktestService>();
builder.Services.AddScoped<StrategyRegimeDiagnosticsService>();
builder.Services.AddSingleton<StrategyOptimizationService>();
builder.Services.AddSingleton<EmaSignalEngine>();
builder.Services.AddHttpClient<IBinanceHistoricalKlineClient, BinanceHistoricalKlineClient>(client =>
{
    client.BaseAddress = new Uri("https://fapi.binance.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EMA-Bot/1.0");
});
// Binance history remains available only for reproducing persisted legacy artifacts.
builder.Services.AddSingleton<BinanceHistoricalMarketDataProvider>();
builder.Services.AddSingleton<IMarketBarStreamProvider>(provider => provider.GetRequiredService<Mt5BridgeMarketBarStreamProvider>());
builder.Services.AddSingleton<IInstrumentCatalogProvider, Mt5BridgeInstrumentCatalogProvider>();
builder.Services.AddSingleton<IMarketQuoteProvider, Mt5BridgeMarketQuoteProvider>();
builder.Services.AddSingleton<IMarketProviderCapabilities, MarketProviderCapabilityService>();
builder.Services.AddSingleton<Mt5BridgeServer>();
builder.Services.AddSingleton<IMt5BridgeRequestClient>(provider => provider.GetRequiredService<Mt5BridgeServer>());
builder.Services.AddSingleton<Mt5ExecutionBridgeServer>();
builder.Services.AddSingleton<IMt5ExecutionBridgeClient>(provider => provider.GetRequiredService<Mt5ExecutionBridgeServer>());
builder.Services.AddScoped<DemoExecutionService>();
builder.Services.AddScoped<IDemoExecutionService>(provider => provider.GetRequiredService<DemoExecutionService>());
builder.Services.AddSingleton<IMt5AccountReader, Mt5BridgeAccountReader>();
builder.Services.AddSingleton<IMt5TradeCalculator, Mt5BridgeTradeCalculator>();
builder.Services.AddSingleton<Mt5BridgeHistoricalMarketDataProvider>();
builder.Services.AddSingleton<Mt5BridgeMarketBarStreamProvider>();
builder.Services.AddSingleton<IHistoricalMarketDataProvider>(provider => provider.GetRequiredService<Mt5BridgeHistoricalMarketDataProvider>());
builder.Services.AddSingleton<IHistoricalMarketDataProviderResolver, HistoricalMarketDataProviderResolver>();
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<PaperTradingCoordinator>();
builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PaperTradingCoordinator>());
builder.Services.AddSingleton<DemoStrategyCoordinator>();
builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DemoStrategyCoordinator>());
builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<Mt5BridgeServer>());
builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<Mt5ExecutionBridgeServer>());
builder.Services.AddHostedService<DemoExecutionRecoveryService>();

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
