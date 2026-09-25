using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class GridBacktestIntegrationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static readonly DateTimeOffset End = Start.AddMinutes(9);
    private static BacktestRequest Request(decimal? balance = 1000m) => new("TESTm", "3m", Start, End, "GRID_RANGE_V1", balance);
    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task Seed(EmaBotDbContext db)
    {
        db.MonitoredSymbols.Add(new() { Symbol = "TESTm", Source = MarketDataSource.Mt5Exness, IsEnabled = true, PaperCommissionPerLotPerSide = 2m });
        await db.SaveChangesAsync();
    }
    private static GridBacktestService Service(EmaBotDbContext db, Native native) => new(db, native, native, native, new(native), Microsoft.Extensions.Logging.Abstractions.NullLogger<GridBacktestService>.Instance);
    private static BacktestsController Controller(EmaBotDbContext db, Native native, BacktestRequestTimeoutOptions? timeout = null)
        => new(db, null!, Options.Create(timeout ?? new()), gridService: Service(db, native));

    [Theory]
    [InlineData(InstrumentTradeMode.Full)] [InlineData(InstrumentTradeMode.LongOnly)] [InlineData(InstrumentTradeMode.ShortOnly)]
    public async Task SuccessfulRunPersistsReloadableGraphWithServerEconomics(InstrumentTradeMode mode)
    {
        await using var db = Database(); await Seed(db); var native = new Native { Mode = mode };
        var run = await Service(db, native).RunAsync("TESTm", "3m", Start, End, 1000m, default);
        db.ChangeTracker.Clear(); var saved = (await Service(db, native).GetAsync(run.Id, default))!;
        Assert.Equal("USD", saved.AccountCurrency); Assert.Equal(2m, saved.CommissionPerLotPerSide);
        Assert.Equal(1000m, saved.StartingBalance); Assert.Equal(50, saved.WarmupCandleCount); Assert.Equal(3, saved.ReportingCandleCount);
        Assert.True(native.HistoryStart < Start); Assert.Equal(Start, saved.RequestedStartUtc); Assert.Equal(End, saved.RequestedEndUtc);
        var cycle = Assert.Single(saved.Cycles); var basket = Assert.Single(cycle.Baskets); var leg = Assert.Single(basket.Legs);
        Assert.Equal(10, cycle.PlannedLevels.Count); Assert.True(leg.FillTimeUtc >= Start);
        Assert.Equal(leg.GrossPnl, basket.GrossPnl); Assert.Equal(leg.TotalCommission, basket.Commission);
        Assert.Equal(basket.NetPnl, saved.NetPnl); Assert.Equal(saved.StartingBalance + saved.NetPnl, saved.EndingBalance);
        Assert.Equal(1, saved.BasketCount); Assert.Equal(1, saved.QualifiedCycles);
        Assert.Equal(3, saved.Events.Count); Assert.Empty(saved.Diagnostics);
        Assert.Equal(5, saved.LevelCount); Assert.Equal(1m, saved.GridBasketRiskPercent);
        if (mode != InstrumentTradeMode.Full)
        {
            Assert.Null(mode == InstrumentTradeMode.LongOnly ? cycle.ShortRisk : cycle.LongRisk);
            Assert.Null(mode == InstrumentTradeMode.LongOnly ? cycle.ShortMargin : cycle.LongMargin);
            Assert.All(cycle.PlannedLevels.Where(p => !p.Allowed), p => { Assert.Null(p.RequiredMargin); Assert.Null(p.InitialStopRisk); });
        }
        Assert.Empty(db.BacktestRuns); Assert.Empty(db.BacktestTrades);
    }

    [Theory]
    [InlineData("disabled")] [InlineData("source")] [InlineData("case")] [InlineData("commission")]
    public async Task ExactEnabledMt5SymbolAndConfiguredCommissionAreRequired(string scenario)
    {
        await using var db = Database(); await Seed(db); var row = await db.MonitoredSymbols.SingleAsync();
        if (scenario == "disabled") row.IsEnabled = false;
        if (scenario == "source") row.Source = MarketDataSource.LegacyBinance;
        if (scenario == "commission") row.PaperCommissionPerLotPerSide = null;
        await db.SaveChangesAsync(); var native = new Native();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db, native).RunAsync(scenario == "case" ? "testm" : "TESTm", "3m", Start, End, 1000m, default));
        Assert.Empty(db.GridBacktestRuns); Assert.Equal(0, native.Calls);
    }

    [Theory]
    [InlineData("balance")] [InlineData("missingBalance")] [InlineData("interval")] [InlineData("dates")] [InlineData("strategy")]
    public async Task InvalidRequestReturns400WithoutRunning(string scenario)
    {
        await using var db = Database(); var native = new Native(); var request = scenario switch
        {
            "balance" => Request(0m), "missingBalance" => Request(null), "interval" => Request() with { Interval = "3d" },
            "dates" => Request() with { EndUtc = Start }, _ => Request() with { StrategyId = "OTHER" }
        };
        Assert.IsType<BadRequestObjectResult>((await Controller(db, native).Run(request, default)).Result);
        Assert.Equal(0, native.Calls);
    }

    [Theory]
    [InlineData("profit")] [InlineData("margin")] [InlineData("exit")]
    public async Task EconomicsFailureNeverPersistsPartialSuccessfulRun(string failure)
    {
        await using var db = Database(); await Seed(db); var native = new Native { Failure = failure };
        var action = (await Controller(db, native).Run(Request(), default)).Result;
        Assert.Equal(503, Assert.IsType<ObjectResult>(action).StatusCode);
        Assert.Empty(db.GridBacktestRuns); Assert.Empty(db.Set<GridBacktestCycle>()); Assert.Empty(db.Set<GridBacktestLeg>()); Assert.Empty(db.Set<GridBacktestTelemetry>());
    }

    [Theory]
    [InlineData(MarketDataErrorKind.RateLimited, 429)] [InlineData(MarketDataErrorKind.Timeout, 504)] [InlineData(MarketDataErrorKind.Unavailable, 503)]
    public async Task ProviderHistoryErrorsMapWithoutSuccess(MarketDataErrorKind kind, int status)
    {
        await using var db = Database(); await Seed(db);
        var result = await Controller(db, new Native { HistoryError = kind }).Run(Request(), default);
        Assert.Equal(status, Assert.IsType<ObjectResult>(result.Result).StatusCode); Assert.Empty(db.GridBacktestRuns);
    }

    [Fact]
    public async Task ClientCancellationPropagatesAndDeadlineMapsTo504()
    {
        await using var db = Database(); await Seed(db);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller(db, new()).Run(Request(), cts.Token));
        var options = new BacktestRequestTimeoutOptions { MinimumRequestTimeout = TimeSpan.FromMilliseconds(30), MaximumRequestTimeout = TimeSpan.FromMilliseconds(30) };
        var result = await Controller(db, new Native { WaitForCancellation = true }, options).Run(Request(), default);
        Assert.Equal(504, Assert.IsType<ObjectResult>(result.Result).StatusCode); Assert.Empty(db.GridBacktestRuns);
    }

    [Fact]
    public void GridBudgetIncludesAllOperationsAndRemainsBounded()
    {
        var options = new BacktestRequestTimeoutOptions();
        var shortBudget = GridBacktestBudget.Calculate("3m", Start, Start.AddMinutes(3), options);
        Assert.True(shortBudget >= TimeSpan.FromSeconds(120));
        Assert.Equal(options.MaximumRequestTimeout, GridBacktestBudget.Calculate("3m", Start, Start.AddMonths(1), options));
        Assert.True(shortBudget <= options.MaximumRequestTimeout);
    }

    [Fact]
    public async Task ExportUsesOnlyPersistedDataAndPreservesNullZeroAndSevenSheets()
    {
        await using var db = Database(); await Seed(db); var native = new Native { Mode = InstrumentTradeMode.LongOnly };
        var run = await Service(db, native).RunAsync("TESTm", "3m", Start, End, 1000m, default);
        var calls = native.Calls; db.ChangeTracker.Clear();
        var workbook = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        Assert.Equal(calls, native.Calls);
        using var zip = new ZipArchive(new MemoryStream(workbook.Bytes));
        static string Read(ZipArchive zip, string name) { using var reader = new StreamReader(zip.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        var names = XDocument.Parse(Read(zip, "xl/workbook.xml")).Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => e.Attribute("name")!.Value);
        Assert.Equal(new[] { "SUMMARY", "CYCLES", "BASKETS", "LEGS", "EVENTS", "DIAGNOSTICS", "TELEMETRY" }, names);
        var summary = Read(zip, "xl/worksheets/sheet1.xml");
        Assert.Contains("GRID_RANGE_V1", summary); Assert.Contains("USD", summary); Assert.DoesNotContain("USDT", summary);
        Assert.DoesNotContain("FeePercentPerSide", summary); Assert.Contains("TOTAL basket", summary);
        var cycle = XDocument.Parse(Read(zip, "xl/worksheets/sheet2.xml"));
        var rows = cycle.Descendants().Where(e => e.Name.LocalName == "row").ToArray();
        var headers = rows[0].Elements().Select(e => e.Value).ToArray(); var values = rows[1].Elements().ToArray();
        Assert.Equal("", values[Array.IndexOf(headers, "ShortRisk")].Value);
        Assert.Equal("0", values[Array.IndexOf(headers, "Adx")].Value);
    }

    [Fact]
    public async Task DeleteRemovesOnlyGridGraphAndKeepsOtherRunsAndSymbols()
    {
        await using var db = Database(); await Seed(db); var service = Service(db, new());
        var first = await service.RunAsync("TESTm", "3m", Start, End, 1000m, default);
        var second = await service.RunAsync("TESTm", "3m", Start, End, 1000m, default);
        db.BacktestRuns.Add(new() { Symbol = "EMA", Interval = "3m" }); await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); Assert.True(await service.DeleteAsync(first.Id, default)); db.ChangeTracker.Clear();
        Assert.Equal(second.Id, (await db.GridBacktestRuns.SingleAsync()).Id); Assert.Single(db.BacktestRuns); Assert.Single(db.MonitoredSymbols);
        Assert.Single(db.Set<GridBacktestCycle>()); Assert.Single(db.Set<GridBacktestBasket>()); Assert.Single(db.Set<GridBacktestLeg>());
        Assert.Equal(10, await db.Set<GridBacktestPlannedLevel>().CountAsync()); Assert.Equal(3, await db.Set<GridBacktestEvent>().CountAsync());
        Assert.False(await service.DeleteAsync(first.Id, default));
    }

    [Fact]
    public void ExactlyOneAdditiveMigrationHasOnlyGridTablesAndCascades()
    {
        using var db = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseMySql("Server=localhost;Database=unused;", new MySqlServerVersion(new Version(8, 4, 0))).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = Assert.Single(assembly.Migrations, p => p.Key.Contains("AddGridHistoricalBacktests"));
        var operations = assembly.CreateMigration(migration.Value, db.Database.ProviderName!).UpOperations;
        Assert.Equal(7, operations.OfType<CreateTableOperation>().Count());
        Assert.All(operations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or AlterDatabaseOperation));
        Assert.All(operations.OfType<CreateTableOperation>(), table =>
        {
            Assert.StartsWith("GridBacktest", table.Name);
            Assert.All(table.ForeignKeys, fk => { Assert.StartsWith("GridBacktest", fk.PrincipalTable); Assert.Equal(ReferentialAction.Cascade, fk.OnDelete); });
        });
        var script = db.GetService<IMigrator>().GenerateScript("20260828151324_AddMt5RiskPercentSizing", migration.Key);
        Assert.Contains("CREATE TABLE `GridBacktestRuns`", script); Assert.DoesNotContain("DROP TABLE", script); Assert.DoesNotContain("ALTER TABLE `Backtest", script);
        Assert.True(operations.OfType<CreateTableOperation>().Single(t => t.Name == "GridBacktestCycles").Columns.Single(c => c.Name == "ShortRisk").IsNullable);
    }

    [Theory]
    [InlineData("GRID_RANGE_V1", 5)] [InlineData("GRID_RANGE_4L_RESEARCH_V1", 4)]
    public async Task G4AHttpRoutesRequireAdminAndReturnFrozenProfile(string strategyId, int levels)
    {
        using var baseFactory = new EmaBotApiFactory(); var native = new Native();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGridHistoricalBarSource>(); services.AddSingleton<IGridHistoricalBarSource>(native);
            services.RemoveAll<IInstrumentCatalogProvider>(); services.AddSingleton<IInstrumentCatalogProvider>(native);
            services.RemoveAll<IMt5AccountReader>(); services.AddSingleton<IMt5AccountReader>(native);
            services.RemoveAll<IMt5TradeCalculator>(); services.AddSingleton<IMt5TradeCalculator>(native);
        }));
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/backtests/grid", "/api/backtests/grid/1", "/api/backtests/grid/1/export/excel" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(AppRoles.Admin, typeof(GridBacktestsController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        using (var scope = factory.Services.CreateScope()) await Seed(scope.ServiceProvider.GetRequiredService<EmaBotDbContext>());
        await Login(client);
        var response = await Send(client, HttpMethod.Post, "/api/backtests", new { strategyId, levelCount = 99, riskPercent = 50m, cooldownBars = 0, stopLevel = 99, atrMultiplier = 9m, symbol = "TESTm", interval = "3m", startUtc = Start, endUtc = End, startingBalance = 1000m, accountCurrency = "FAKE", commissionPerLotPerSide = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(strategyId, json.GetProperty("strategyId").GetString());
        Assert.Equal(strategyId, json.GetProperty("run").GetProperty("strategyId").GetString());
        Assert.Equal(levels, json.GetProperty("run").GetProperty("levelCount").GetInt32());
        Assert.Equal(1m, json.GetProperty("run").GetProperty("gridBasketRiskPercent").GetDecimal());
        Assert.Equal(3, json.GetProperty("run").GetProperty("cooldownBars").GetInt32());
        Assert.Equal("USD", json.GetProperty("run").GetProperty("accountCurrency").GetString());
        Assert.Equal(2m, json.GetProperty("run").GetProperty("commissionPerLotPerSide").GetDecimal());
        Assert.DoesNotContain("ema9", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("crossover", text, StringComparison.OrdinalIgnoreCase);
        var id = json.GetProperty("run").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/backtests/grid/{id}")).StatusCode);
        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/backtests/grid")).EnumerateArray());
        Assert.Equal(strategyId, listed.GetProperty("strategyId").GetString());
        var unknown = await Send(client, HttpMethod.Post, "/api/backtests", Request() with { StrategyId = "GRID_RANGE_3L_RESEARCH_V1" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/backtests/grid/{id}/export/excel")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, $"/api/backtests/grid/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/backtests/grid/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(null)] [InlineData("EMA_TREND_V1")]
    public async Task EmaStrategyDefaultAndExplicitKeepExistingResponse(string? strategy)
    {
        using var factory = new EmaBotApiFactory(); using var client = factory.CreateClient(); await Login(client);
        using (var scope = factory.Services.CreateScope()) await Seed(scope.ServiceProvider.GetRequiredService<EmaBotDbContext>());
        factory.BinanceClient.Klines = Enumerable.Range(0, 60).Select(i => new Candle(Start.AddMinutes(i), Start.AddMinutes(i + 1).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true)).ToArray();
        var response = await Send(client, HttpMethod.Post, "/api/backtests", new BacktestRequest("TESTm", "3m", Start, Start.AddHours(1), strategy));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.TryGetProperty("trades", out _)); Assert.False(json.TryGetProperty("cycles", out _));
    }
    private static async Task Login(HttpClient client)
    {
        var response = await Send(client, HttpMethod.Post, "/api/auth/login", new LoginRequest("admin", "A-strong-password-123!"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var message = new HttpRequestMessage(method, path); if (body is not null) message.Content = JsonContent.Create(body);
        message.Headers.Add("X-CSRF-TOKEN", csrf!.Token); return await client.SendAsync(message);
    }

    [Theory]
    [InlineData("profit", "RiskCalculationUnavailable", "CalculateProfit")]
    [InlineData("margin", "MarginCalculationUnavailable", "CalculateMargin")]
    public async Task G21UnavailableEconomicsLogsOnlySafeStructuredEvidence(string failure, string code, string operation)
    {
        await using var db = Database(); await Seed(db);
        var native = new Native { Failure = failure }; var logger = new EvidenceLogger();
        var service = new GridBacktestService(db, native, native, native, new(native), logger);
        await Assert.ThrowsAsync<GridNativeEconomicsUnavailableException>(() => service.RunAsync("TESTm", "3m", Start, End, 1000m, default));
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
        Assert.Equal(code, entry["DiagnosticCode"]); Assert.Equal(operation, entry["Operation"]);
        Assert.Equal("TESTm", entry["BrokerSymbol"]); Assert.Equal("Long", entry["Direction"]?.ToString());
        Assert.True(Assert.IsType<decimal>(entry["Lots"]) > 0m);
        Assert.Equal(6, entry.Count); // Five whitelisted fields plus logging's OriginalFormat.
        Assert.DoesNotContain("secret", string.Join(" ", entry.Values));
        });
        Assert.Empty(db.GridBacktestRuns);
    }

    private sealed class EvidenceLogger : Microsoft.Extensions.Logging.ILogger<GridBacktestService>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, level);
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(p => p.Key, p => p.Value));
        }
    }

    [Theory]
    [InlineData("GRID_RANGE_V1", 5)] [InlineData("GRID_RANGE_4L_RESEARCH_V1", 4)]
    public async Task G4APersistenceAndExportUseActualProfileAndPreserveOtherSavedRuns(string strategyId, int levels)
    {
        await using var db = Database(); await Seed(db);
        var native = new Native { Mode = InstrumentTradeMode.Full }; var service = Service(db, native);
        var old = await service.RunAsync("TESTm", "3m", Start, End, 1000m, default);
        var run = await service.RunAsync("TESTm", "3m", Start, End, 1000m, default, strategyId);
        db.ChangeTracker.Clear(); run = (await service.GetAsync(run.Id, default))!;
        Assert.Equal(strategyId, run.StrategyId); Assert.Equal(levels, run.LevelCount);
        Assert.Equal(1m, run.GridBasketRiskPercent); Assert.Equal(3, run.CooldownBars);
        var cycle = Assert.Single(run.Cycles);
        Assert.Equal(2 * levels, cycle.PlannedLevels.Count);
        Assert.Equal(levels, cycle.PlannedLevels.Count(l => l.Direction == "Long"));
        Assert.Equal(levels, cycle.PlannedLevels.Count(l => l.Direction == "Short"));
        Assert.Equal(100m - (levels + 1) * 2m, cycle.LongStop);
        Assert.Equal(100m + (levels + 1) * 2m, cycle.ShortStop);
        var calls = native.Calls;
        var workbook = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        Assert.Equal(calls, native.Calls);
        using var zip = new ZipArchive(new MemoryStream(workbook.Bytes));
        static string Read(ZipArchive zip, string path) { using var reader = new StreamReader(zip.GetEntry(path)!.Open()); return reader.ReadToEnd(); }
        var summary = Read(zip, "xl/worksheets/sheet1.xml");
        Assert.Contains(strategyId, summary); Assert.Contains($"{levels} equal-lot levels", summary);
        Assert.Contains($"stop level {levels + 1}", summary);
        var rows = XDocument.Parse(Read(zip, "xl/worksheets/sheet2.xml")).Descendants().Where(e => e.Name.LocalName == "row").ToArray();
        var headers = rows[0].Elements().Select(e => e.Value).Where(v => v.EndsWith("PlannedPrice")).ToArray();
        Assert.Equal(new[] { "Long", "Short" }.SelectMany(d => Enumerable.Range(1, levels).Select(n => $"{d}{n}PlannedPrice")), headers);
        Assert.Equal(rows[0].Elements().Count(), rows[1].Elements().Count());
        Assert.True(await service.DeleteAsync(run.Id, default)); db.ChangeTracker.Clear();
        var baseline = (await service.GetAsync(old.Id, default))!;
        Assert.Equal("GRID_RANGE_V1", baseline.StrategyId); Assert.Equal(5, baseline.LevelCount);
        Assert.Equal(10, Assert.Single(baseline.Cycles).PlannedLevels.Count);
    }

    [Fact]
    public void G4APublicRequestHasNoRawStrategyParameters()
        => Assert.Equal(new[] { "Symbol", "Interval", "StartUtc", "EndUtc", "StrategyId", "StartingBalance" },
            typeof(BacktestRequest).GetProperties().Select(p => p.Name));

    [Theory]
    [InlineData("GRID_RANGE_V1")] [InlineData("GRID_RANGE_4L_RESEARCH_V1")]
    public async Task G4B0ServiceSavesTelemetryInSameSingleGraphSave(string strategyId)
    {
        var saves = new TelemetrySaveCounter();
        await using var db = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(saves).Options);
        await Seed(db); var native = new Native();
        var run = await Service(db, native).RunAsync("TESTm", "3m", Start, End, 1000m, default, strategyId);
        Assert.Equal(2, saves.Calls); // Seed plus exactly one complete run graph.
        Assert.Equal(2, saves.TelemetryRowsInLastSave);
        Assert.Equal(2, await db.Set<GridBacktestTelemetry>().CountAsync());
        Assert.All(await db.Set<GridBacktestTelemetry>().ToListAsync(), t => Assert.Equal(run.Cycles.Single().Id, t.GridBacktestCycleId));
    }

    private sealed class TelemetrySaveCounter : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public int TelemetryRowsInLastSave { get; private set; }
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken token = default)
        {
            Calls++;
            TelemetryRowsInLastSave = data.Context!.ChangeTracker.Entries<GridBacktestTelemetry>().Count(e => e.State == EntityState.Added);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Native : IGridHistoricalBarSource, IInstrumentCatalogProvider, IMt5AccountReader, IMt5TradeCalculator
    {
        public InstrumentTradeMode Mode { get; init; } = InstrumentTradeMode.LongOnly;
        public string? Failure { get; init; }
        public MarketDataErrorKind? HistoryError { get; init; }
        public bool WaitForCancellation { get; init; }
        public DateTimeOffset HistoryStart { get; private set; }
        public int Calls { get; private set; }
        public async Task<IReadOnlyList<Mt5HistoricalExecutionBar>> GetAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            HistoryStart = start;
            if (WaitForCancellation) { var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var registration = token.Register(() => done.TrySetCanceled(token)); await done.Task; }
            if (HistoryError is { } kind) throw new MarketDataProviderException("history", kind, "test");
            Mt5HistoricalExecutionBar Bar(int i, decimal low, decimal high, decimal close) => new(symbol, interval, Start.AddMinutes(i * 3), Start.AddMinutes((i + 1) * 3).AddMilliseconds(-1), close, high, low, close, 100, 0, true);
            var bars = Enumerable.Range(-50, 51).Select(i => Bar(i, 98m, 102m, 100m)).ToList();
            bars.Add(Mode == InstrumentTradeMode.ShortOnly ? Bar(1, 101m, 102m, 101m) : Bar(1, 98m, 99m, 99m));
            bars.Add(Bar(2, 99m, 101m, 100m)); return bars;
        }
        public Task<InstrumentCatalogItem?> GetAsync(string symbol, CancellationToken token) => Task.FromResult<InstrumentCatalogItem?>(new(
            new("Exness", symbol, symbol, AssetClass.Forex, 2, .01m, 100000m, .01m, 10m, .01m, "EUR", "USD", "USD", HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, Mode));
        public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<Mt5AccountPayload> GetAsync(CancellationToken token) => Task.FromResult(new Mt5AccountPayload(1, "test", "USD", 99999m, 99999m, 0m, 99999m, 0m, "Demo"));
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Calls++; if (Failure == "margin") throw new InvalidOperationException("secret must not leak");
            return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.VolumeLots * 100m, "USD"));
        }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Calls++; var pnl = (r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m * (r.Direction == "Long" ? 1m : -1m);
            if (Failure == "profit" || Failure == "exit" && pnl > 0m) throw new InvalidOperationException("secret must not leak");
            return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice, pnl, "USD"));
        }
    }
}
