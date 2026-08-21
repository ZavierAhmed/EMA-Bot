using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DatabaseInitializerRestartTests(EmaBotApiFactory factory) : IClassFixture<EmaBotApiFactory>
{
    [Fact]
    public async Task StartAsync_RunningDemoStrategySessionFailsClosedAndExpiresUnlinkedIntent()
    {
        var clientExecutionId = Guid.NewGuid();
        int sessionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var session = new DemoStrategySession
            {
                Interval = "3m", Status = DemoStrategySessionStatus.Running, CreatedAtUtc = now, StartedAtUtc = now,
                FixedLots = .01m, RiskReward = 2m, WaitForConfirmationCandle = true,
                Symbols = [new DemoStrategySessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm" }]
            };
            database.DemoStrategySessions.Add(session);
            await database.SaveChangesAsync();
            database.DemoStrategyIntents.Add(new DemoStrategyIntent
            {
                DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = session.Symbols.Single().Id,
                Direction = SignalDirection.Long, CrossoverTimeUtc = now.AddMinutes(-6), SignalTimeUtc = now.AddMinutes(-3), ExpectedEntryOpenUtc = now,
                SignalOpen = 100m, SignalClose = 100m, SignalGapState = GapState.Unchanged, StructuralStopLoss = 99m,
                StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = now.AddMinutes(-3), IntendedVolumeLots = .01m,
                ClientExecutionId = clientExecutionId, Status = DemoStrategyIntentStatus.WaitingForEntryWindow, CreatedAtUtc = now
            });
            await database.SaveChangesAsync(); sessionId = session.Id;
        }

        var initializer = new DatabaseInitializer(factory.Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new BootstrapAdminOptions()), NullLogger<DatabaseInitializer>.Instance);
        await initializer.StartAsync(default);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var sessionAfter = await verify.DemoStrategySessions.SingleAsync(item => item.Id == sessionId);
        var intentAfter = await verify.DemoStrategyIntents.SingleAsync(item => item.DemoStrategySessionId == sessionId);
        Assert.Equal(DemoStrategySessionStatus.Interrupted, sessionAfter.Status);
        Assert.NotNull(sessionAfter.InterruptedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(sessionAfter.FailureMessage));
        Assert.Equal(DemoStrategyIntentStatus.Expired, intentAfter.Status);
        Assert.Equal(clientExecutionId, intentAfter.ClientExecutionId);
        Assert.Null(intentAfter.DemoExecutionId);
        Assert.Equal(0, await verify.DemoExecutions.CountAsync(item => item.ClientExecutionId == clientExecutionId));
    }
}
