using System.Net;
using System.Net.Http.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmaBot.Api.Tests;

public sealed class AuthEndpointTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;

    public AuthEndpointTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CurrentUser_RejectsUnauthenticatedRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TradeExplorer_RejectsUnauthenticatedRequests()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trades")).StatusCode);
    }

    [Fact]
    public async Task Login_RejectsInvalidPassword()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);

        var response = await PostLogin(client, token, "admin", "incorrect-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanLogInAndLoadCurrentUser()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);

        var login = await PostLogin(client, token, "admin", "A-strong-password-123!");
        var currentUser = await client.GetAsync("/api/auth/me");

        Assert.True(login.StatusCode == HttpStatusCode.NoContent, await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);
        var payload = await currentUser.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(payload);
        Assert.Equal("admin", payload.UserName);
        Assert.Equal(AppRoles.Admin, payload.Role);
    }

    [Fact]
    public async Task TradingSettings_AreProtectedAndPersistUpdates()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/settings/trading")).StatusCode);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);
        Assert.Equal(HttpStatusCode.NoContent, (await PostLogin(client, token, "admin", "A-strong-password-123!")).StatusCode);
        var updateToken = await GetAntiforgeryToken(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings/trading") { Content = JsonContent.Create(new { riskReward = 3m, fixedOrderSizeUsdt = 250m, waitForConfirmationCandle = false, useEma100Filter = true, trailingStopEnabled = false }) };
        request.Headers.Add("X-CSRF-TOKEN", updateToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        var settings = await client.GetFromJsonAsync<TradingSettingsResponse>("/api/settings/trading");
        Assert.Equal(3m, settings?.RiskReward);
        Assert.True(settings?.UseEma100Filter);
    }

    [Fact]
    public async Task Symbols_OnlyExposeExistingMonitoredInstrumentActions()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);
        Assert.Equal(HttpStatusCode.NoContent, (await PostLogin(client, token, "admin", "A-strong-password-123!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostJson(client, "/api/symbols", "BTCUSDT")).StatusCode);
    }

    [Fact]
    public async Task PaperSessionStart_IsUnavailableBeforePersistence()
    {
        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDatabase = setupScope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            if (!await setupDatabase.MonitoredSymbols.AnyAsync(symbol => symbol.Symbol == "BTCUSDT")) setupDatabase.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", IsEnabled = true });
            if (!await setupDatabase.MonitoredSymbols.AnyAsync(symbol => symbol.Symbol == "ETHUSDT")) setupDatabase.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "ETHUSDT", BaseAsset = "ETH", QuoteAsset = "USDT", IsEnabled = true });
            await setupDatabase.SaveChangesAsync();
        }
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var before = await database.PaperSessions.CountAsync();
        var symbolsBefore = await database.PaperSessionSymbols.CountAsync();
        var controller = new PaperSessionsController(database, scope.ServiceProvider.GetRequiredService<TradingSettingsService>(), scope.ServiceProvider.GetRequiredService<PaperTradingCoordinator>(), new TestBinanceStreamClient(), TestMarketProviderCapabilities.WithLiveBars(false));
        var response = await controller.Start(new StartPaperSessionRequest("3m", ["BTCUSDT", "ETHUSDT"]), CancellationToken.None);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Equal(before, await database.PaperSessions.CountAsync());
        Assert.Equal(symbolsBefore, await database.PaperSessionSymbols.CountAsync());
    }

    [Fact]
    public async Task PaperSessionResume_IsUnavailableWithoutChangingPersistedState()
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var session = new PaperSession { Interval = "3m", Status = PaperSessionStatus.Interrupted, StartedAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, FixedOrderSizeUsdt = 100m };
        database.PaperSessions.Add(session); await database.SaveChangesAsync();
        var controller = new PaperSessionsController(database, scope.ServiceProvider.GetRequiredService<TradingSettingsService>(), scope.ServiceProvider.GetRequiredService<PaperTradingCoordinator>(), new TestBinanceStreamClient(), TestMarketProviderCapabilities.WithLiveBars(false));

        var response = await controller.Resume(session.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Equal(PaperSessionStatus.Interrupted, (await database.PaperSessions.FindAsync(session.Id))!.Status);
    }

    [Fact]
    public async Task PaperSessionStart_DoesNotTrustCapabilityWithoutAConfiguredStream()
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var before = await database.PaperSessions.CountAsync();
        var controller = new PaperSessionsController(database, scope.ServiceProvider.GetRequiredService<TradingSettingsService>(), scope.ServiceProvider.GetRequiredService<PaperTradingCoordinator>(), new UnavailableMarketBarStreamProvider(), TestMarketProviderCapabilities.WithLiveBars(true));

        var response = await controller.Start(new StartPaperSessionRequest("3m", ["BTCUSDT"]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Equal(before, await database.PaperSessions.CountAsync());
    }

    [Fact]
    public async Task StrategyPreview_UsesHistoricalProviderForEnabledMonitoredInstrument()
    {
        _factory.BinanceClient.Klines = Enumerable.Range(0, 105).Select(index =>
        {
            var time = DateTimeOffset.UnixEpoch.AddMinutes(index * 3);
            var close = index < 100 ? 100m : 100m + index - 99m;
            return new Market.Candle(time, time.AddMinutes(3).AddMilliseconds(-1), close - 1m, close + 1m, close - 2m, close, 1m, true);
        }).ToArray();
        using (var setupScope = _factory.Services.CreateScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            if (!await database.MonitoredSymbols.AnyAsync(symbol => symbol.Symbol == "BTCUSDT")) database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", IsEnabled = true });
            await database.SaveChangesAsync();
        }
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);
        Assert.Equal(HttpStatusCode.NoContent, (await PostLogin(client, token, "admin", "A-strong-password-123!")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/strategy/preview?symbol=BTCUSDT&interval=3m&limit=105")).StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var monitored = await verifyScope.ServiceProvider.GetRequiredService<EmaBotDbContext>().MonitoredSymbols.SingleAsync(symbol => symbol.Symbol == "BTCUSDT");
        monitored.IsEnabled = false; await verifyScope.ServiceProvider.GetRequiredService<EmaBotDbContext>().SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/strategy/preview?symbol=BTCUSDT&interval=3m&limit=105")).StatusCode);
    }

    private static async Task<string> GetAntiforgeryToken(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var token = System.Text.Json.JsonSerializer.Deserialize<AntiforgeryResponse>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return Assert.IsType<string>(token?.Token);
    }

    private static async Task<HttpResponseMessage> PostLogin(HttpClient client, string token, string userName, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(userName, password))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, string symbol)
    {
        var token = await GetAntiforgeryToken(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { symbol }) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}

public sealed class EmaBotApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"EmaBotApiTests-{Guid.NewGuid()}";
    public TestBinanceClient BinanceClient { get; } = new();
    public TestBinanceStreamClient StreamClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Mt5Bridge:Enabled", "false");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IBinanceHistoricalKlineClient>();
            services.AddSingleton<IBinanceHistoricalKlineClient>(BinanceClient);
            services.RemoveAll<IHistoricalMarketDataProvider>();
            services.AddSingleton<IHistoricalMarketDataProvider>(provider => provider.GetRequiredService<BinanceHistoricalMarketDataProvider>());
            services.RemoveAll<IMarketBarStreamProvider>();
            services.AddSingleton<IMarketBarStreamProvider>(StreamClient);
            services.RemoveAll(typeof(DbContextOptions<EmaBotDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<EmaBotDbContext>));
            services.RemoveAll<IHostedService>();
            services.AddDbContext<EmaBotDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            database.Database.EnsureDeleted();
            database.Database.EnsureCreated();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            roleManager.CreateAsync(new IdentityRole(AppRoles.Admin)).GetAwaiter().GetResult();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<EmaUser>>();
            var admin = new EmaUser
            {
                UserName = "admin",
                Email = "admin@example.test",
                EmailConfirmed = true,
                IsActive = true
            };
            userManager.CreateAsync(admin, "A-strong-password-123!").GetAwaiter().GetResult();
            userManager.AddToRoleAsync(admin, AppRoles.Admin).GetAwaiter().GetResult();
        });
    }

}
