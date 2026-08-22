using EmaBot.Api.Auth;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EmaBot.Api.Data;

public sealed class EmaBotDbContext(DbContextOptions<EmaBotDbContext> options)
    : IdentityDbContext<EmaUser, IdentityRole, string>(options)
{
    public DbSet<MonitoredSymbol> MonitoredSymbols => Set<MonitoredSymbol>();
    public DbSet<TradingSettings> TradingSettings => Set<TradingSettings>();
    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();
    public DbSet<BacktestTrade> BacktestTrades => Set<BacktestTrade>();
    public DbSet<BacktestTradeEvent> BacktestTradeEvents => Set<BacktestTradeEvent>();
    public DbSet<PaperSession> PaperSessions => Set<PaperSession>();
    public DbSet<PaperSessionSymbol> PaperSessionSymbols => Set<PaperSessionSymbol>();
    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();
    public DbSet<PaperTradeEvent> PaperTradeEvents => Set<PaperTradeEvent>();
    public DbSet<PaperDecisionEvent> PaperDecisionEvents => Set<PaperDecisionEvent>();
    public DbSet<DemoExecution> DemoExecutions => Set<DemoExecution>();
    public DbSet<DemoExecutionManagementAction> DemoExecutionManagementActions => Set<DemoExecutionManagementAction>();
    public DbSet<DemoStrategySession> DemoStrategySessions => Set<DemoStrategySession>();
    public DbSet<DemoStrategySessionSymbol> DemoStrategySessionSymbols => Set<DemoStrategySessionSymbol>();
    public DbSet<DemoStrategyIntent> DemoStrategyIntents => Set<DemoStrategyIntent>();
    public DbSet<DemoStrategyPositionManagement> DemoStrategyPositionManagement => Set<DemoStrategyPositionManagement>();
    public DbSet<StrategyOptimizationRun> StrategyOptimizationRuns => Set<StrategyOptimizationRun>();
    public DbSet<StrategyOptimizationCandidate> StrategyOptimizationCandidates => Set<StrategyOptimizationCandidate>();
    public DbSet<StrategyOptimizationMarketResult> StrategyOptimizationMarketResults => Set<StrategyOptimizationMarketResult>();
    public DbSet<StrategyOptimizationTrade> StrategyOptimizationTrades => Set<StrategyOptimizationTrade>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmaUser>(entity =>
        {
            entity.Property(user => user.IsActive).HasDefaultValue(true);
        });

        builder.Entity<MonitoredSymbol>(entity =>
        {
            entity.HasIndex(symbol => new { symbol.Source, symbol.Symbol }).IsUnique();
            entity.Property(symbol => symbol.Source).HasConversion<string>().HasMaxLength(32).HasDefaultValue(MarketDataSource.LegacyBinance);
            entity.Property(symbol => symbol.Symbol).HasMaxLength(64).UseCollation("utf8mb4_bin");
            entity.Property(symbol => symbol.DisplayName).HasMaxLength(256);
            entity.Property(symbol => symbol.BaseAsset).HasMaxLength(32);
            entity.Property(symbol => symbol.QuoteAsset).HasMaxLength(16);
            entity.Property(symbol => symbol.PaperCommissionPerLotPerSide).HasPrecision(18, 8);
        });

        builder.Entity<TradingSettings>(entity =>
        {
            entity.HasKey(settings => settings.Id);
            entity.ToTable(table => table.HasCheckConstraint("CK_TradingSettings_Singleton", "`Id` = 1"));
            entity.Property(settings => settings.Id).ValueGeneratedNever();
            entity.Property(settings => settings.RiskReward).HasPrecision(18, 8);
            entity.Property(settings => settings.FixedOrderSizeUsdt).HasPrecision(18, 8);
            entity.Property(settings => settings.MinEmaGapPercent).HasPrecision(8, 4);
            entity.Property(settings => settings.MaxStopDistancePercent).HasPrecision(8, 4);
            entity.Property(settings => settings.SimulatedAccountBalanceUsdt).HasPrecision(18, 8);
            entity.Property(settings => settings.MarginPerTradePercent).HasPrecision(8, 4);
            entity.Property(settings => settings.Leverage).HasPrecision(8, 4);
            entity.Property(settings => settings.FeePercentPerSide).HasPrecision(8, 4);
            entity.Property(settings => settings.PaperFixedLots).HasPrecision(18, 8);
            entity.Property(settings => settings.PaperMarginPerTradePercent).HasPrecision(8, 4);
            entity.Property(settings => settings.PaperStartingBalance).HasPrecision(18, 8);
            entity.Property(settings => settings.MaxReentryAgeBars).HasDefaultValue(6);
            entity.Property(settings => settings.ExitOnOppositeCrossover).HasDefaultValue(false);
        });
        builder.Entity<BacktestRun>(entity => { entity.Property(run => run.MarketDataSource).HasConversion<string>().HasMaxLength(32).HasDefaultValue(MarketDataSource.LegacyBinance); entity.Property(run => run.Symbol).HasMaxLength(64); entity.Property(run => run.Interval).HasMaxLength(8); entity.Property(run => run.MinEmaGapPercent).HasPrecision(8, 4); entity.Property(run => run.MaxStopDistancePercent).HasPrecision(8, 4); entity.Property(run => run.StartingBalanceUsdt).HasPrecision(18, 8); entity.Property(run => run.EndingBalanceUsdt).HasPrecision(18, 8); entity.Property(run => run.MarginPerTradePercent).HasPrecision(8, 4); entity.Property(run => run.Leverage).HasPrecision(8, 4); entity.Property(run => run.MaxReentryAgeBars).HasDefaultValue(6); entity.Property(run => run.ExitOnOppositeCrossover).HasDefaultValue(false); entity.HasMany(run => run.Trades).WithOne(trade => trade.BacktestRun!).HasForeignKey(trade => trade.BacktestRunId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<BacktestTrade>(entity => { entity.Property(trade => trade.AccountEquityAtEntryUsdt).HasPrecision(18, 8); entity.Property(trade => trade.MarginUsedUsdt).HasPrecision(18, 8); entity.Property(trade => trade.Leverage).HasPrecision(8, 4); entity.Property(trade => trade.SignalOpen).HasPrecision(18, 8); entity.Property(trade => trade.SignalAtr14).HasPrecision(18, 8); entity.Property(trade => trade.ReversalPowerScore).HasPrecision(8, 4); entity.Property(trade => trade.StopAnchorPrice).HasPrecision(18, 8); entity.Property(trade => trade.StopBuffer).HasPrecision(18, 8); entity.HasIndex(trade => trade.BacktestRunId); entity.HasMany(trade => trade.Events).WithOne(item => item.BacktestTrade!).HasForeignKey(item => item.BacktestTradeId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<BacktestTradeEvent>(entity => entity.HasIndex(item => item.BacktestTradeId));
        builder.Entity<PaperSession>(entity =>
        {
            entity.Property(session => session.MarketDataSource).HasConversion<string>().HasMaxLength(32).HasDefaultValue(MarketDataSource.LegacyBinance);
            entity.Property(session => session.Interval).HasMaxLength(8);
            entity.Property(session => session.FeePercentPerSide).HasPrecision(8, 4);
            entity.Property(session => session.MinEmaGapPercent).HasPrecision(8, 4);
            entity.Property(session => session.MaxStopDistancePercent).HasPrecision(8, 4);
            entity.Property(session => session.StartingBalanceUsdt).HasPrecision(18, 8);
            entity.Property(session => session.CurrentBalanceUsdt).HasPrecision(18, 8);
            entity.Property(session => session.MarginPerTradePercent).HasPrecision(8, 4);
            entity.Property(session => session.Leverage).HasPrecision(8, 4);
            entity.Property(session => session.UsedMarginUsdt).HasPrecision(18, 8);
            entity.Property(session => session.AccountCurrency).HasMaxLength(16).HasDefaultValue("USDT");
            entity.Property(session => session.PaperFixedLots).HasPrecision(18, 8);
            entity.Property(session => session.PaperMarginPerTradePercent).HasPrecision(8, 4);
            entity.Property(session => session.StartingBalance).HasPrecision(18, 8);
            entity.Property(session => session.CurrentBalance).HasPrecision(18, 8);
            entity.Property(session => session.UsedMargin).HasPrecision(18, 8);
            entity.Property(session => session.NetPnl).HasPrecision(18, 8);
            entity.Property(session => session.TotalTradingCosts).HasPrecision(18, 8);
            entity.Property(session => session.MaxReentryAgeBars).HasDefaultValue(6);
            entity.Property(session => session.ExitOnOppositeCrossover).HasDefaultValue(false);
            entity.Property(session => session.RejectedByExecutableStop).HasDefaultValue(0);
            entity.HasIndex(session => session.Status);
            entity.HasMany(session => session.Symbols).WithOne(symbol => symbol.PaperSession!).HasForeignKey(symbol => symbol.PaperSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.Trades).WithOne(trade => trade.PaperSession!).HasForeignKey(trade => trade.PaperSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.DecisionEvents).WithOne(item => item.PaperSession!).HasForeignKey(item => item.PaperSessionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PaperSessionSymbol>(entity =>
        {
            entity.Property(symbol => symbol.Symbol).HasMaxLength(32);
            entity.Property(symbol => symbol.BrokerSymbol).HasMaxLength(64).UseCollation("utf8mb4_bin");
            entity.Property(symbol => symbol.ContractSize).HasPrecision(18, 8);
            entity.Property(symbol => symbol.VolumeMin).HasPrecision(18, 8);
            entity.Property(symbol => symbol.VolumeMax).HasPrecision(18, 8);
            entity.Property(symbol => symbol.VolumeStep).HasPrecision(18, 8);
            entity.Property(symbol => symbol.VolumeLimit).HasPrecision(18, 8);
            entity.Property(symbol => symbol.TickSize).HasPrecision(18, 8);
            entity.Property(symbol => symbol.TickValueProfit).HasPrecision(18, 8);
            entity.Property(symbol => symbol.TickValueLoss).HasPrecision(18, 8);
            entity.Property(symbol => symbol.PointSize).HasPrecision(18, 8);
            entity.Property(symbol => symbol.CommissionPerLotPerSide).HasPrecision(18, 8);
            entity.HasIndex(symbol => new { symbol.PaperSessionId, symbol.Symbol }).IsUnique();
            entity.HasMany(symbol => symbol.Trades).WithOne(trade => trade.PaperSessionSymbol!).HasForeignKey(trade => trade.PaperSessionSymbolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(symbol => symbol.DecisionEvents).WithOne(item => item.PaperSessionSymbol!).HasForeignKey(item => item.PaperSessionSymbolId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PaperTrade>(entity =>
        {
            entity.Property(trade => trade.Symbol).HasMaxLength(32);
            entity.Property(trade => trade.Interval).HasMaxLength(8);
            entity.Property(trade => trade.AccountEquityAtEntryUsdt).HasPrecision(18, 8);
            entity.Property(trade => trade.MarginUsedUsdt).HasPrecision(18, 8);
            entity.Property(trade => trade.Leverage).HasPrecision(8, 4);
            entity.Property(trade => trade.SignalOpen).HasPrecision(18, 8);
            entity.Property(trade => trade.Lots).HasPrecision(18, 8);
            entity.Property(trade => trade.EntryBid).HasPrecision(18, 8);
            entity.Property(trade => trade.EntryAsk).HasPrecision(18, 8);
            entity.Property(trade => trade.EntrySpread).HasPrecision(18, 8);
            entity.Property(trade => trade.ExitBid).HasPrecision(18, 8);
            entity.Property(trade => trade.ExitAsk).HasPrecision(18, 8);
            entity.Property(trade => trade.ExitSpread).HasPrecision(18, 8);
            entity.Property(trade => trade.RequiredMargin).HasPrecision(18, 8);
            entity.Property(trade => trade.RoundTripCommission).HasPrecision(18, 8);
            entity.Property(trade => trade.GrossPnl).HasPrecision(18, 8);
            entity.Property(trade => trade.NetPnl).HasPrecision(18, 8);
            entity.Property(trade => trade.AccountEquityAtEntry).HasPrecision(18, 8);
            entity.Property(trade => trade.MarginUsed).HasPrecision(18, 8);
            entity.Property(trade => trade.InitialRiskAmount).HasPrecision(18, 8);
            entity.Property(trade => trade.SignalAtr14).HasPrecision(18, 8);
            entity.Property(trade => trade.ReversalPowerScore).HasPrecision(8, 4);
            entity.Property(trade => trade.StopAnchorPrice).HasPrecision(18, 8);
            entity.Property(trade => trade.StopBuffer).HasPrecision(18, 8);
            entity.HasIndex(trade => new { trade.PaperSessionSymbolId, trade.Status });
            entity.HasMany(trade => trade.Events).WithOne(item => item.PaperTrade!).HasForeignKey(item => item.PaperTradeId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PaperDecisionEvent>(entity =>
        {
            entity.Property(item => item.Stage).HasMaxLength(64);
            entity.Property(item => item.Message).HasMaxLength(1024);
            entity.Property(item => item.Ema9).HasPrecision(18, 8);
            entity.Property(item => item.Ema15).HasPrecision(18, 8);
            entity.Property(item => item.Ema100).HasPrecision(18, 8);
            entity.Property(item => item.GapPercent).HasPrecision(18, 8);
            entity.Property(item => item.StopPrice).HasPrecision(18, 8);
            entity.Property(item => item.Bid).HasPrecision(18, 8);
            entity.Property(item => item.Ask).HasPrecision(18, 8);
            entity.Property(item => item.EntryPrice).HasPrecision(18, 8);
            entity.Property(item => item.Lots).HasPrecision(18, 8);
            entity.Property(item => item.RequiredMargin).HasPrecision(18, 8);
            entity.HasIndex(item => new { item.PaperSessionId, item.TimeUtc });
            entity.HasIndex(item => new { item.PaperSessionSymbolId, item.TimeUtc });
        });
        builder.Entity<DemoExecution>(entity =>
        {
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Provider).HasMaxLength(32);
            entity.Property(item => item.ExpectedAccountFingerprint).HasMaxLength(128);
            entity.Property(item => item.ExpectedServer).HasMaxLength(256);
            entity.Property(item => item.BrokerSymbol).HasMaxLength(64).UseCollation("utf8mb4_bin");
            entity.Property(item => item.Side).HasMaxLength(8);
            entity.Property(item => item.VolumeLots).HasPrecision(18, 8);
            entity.Property(item => item.RequestedStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.RequestedTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.CurrentStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.CurrentTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.CorrelationMarker).HasMaxLength(96);
            entity.Property(item => item.FilledVolumeLots).HasPrecision(18, 8);
            entity.Property(item => item.AverageFillPrice).HasPrecision(18, 8);
            entity.Property(item => item.ClosedVolumeLots).HasPrecision(18, 8);
            entity.Property(item => item.AverageClosePrice).HasPrecision(18, 8);
            entity.Property(item => item.BrokerRetcode).HasMaxLength(64);
            entity.Property(item => item.BrokerMessage).HasMaxLength(1024);
            entity.Property(item => item.BrokerAccountCurrency).HasMaxLength(16);
            entity.Property(item => item.BrokerEntryProfit).HasPrecision(18, 8);
            entity.Property(item => item.BrokerEntryCommission).HasPrecision(18, 8);
            entity.Property(item => item.BrokerEntrySwap).HasPrecision(18, 8);
            entity.Property(item => item.BrokerEntryFee).HasPrecision(18, 8);
            entity.Property(item => item.BrokerCurrentProfit).HasPrecision(18, 8);
            entity.Property(item => item.BrokerCurrentSwap).HasPrecision(18, 8);
            entity.Property(item => item.BrokerHistoryProfit).HasPrecision(18, 8);
            entity.Property(item => item.BrokerHistoryCommission).HasPrecision(18, 8);
            entity.Property(item => item.BrokerHistorySwap).HasPrecision(18, 8);
            entity.Property(item => item.BrokerHistoryFee).HasPrecision(18, 8);
            entity.Property(item => item.NativeExitReason).HasMaxLength(64);
            entity.Property(item => item.NativeExitReasonConflicted).HasDefaultValue(false);
            entity.Property(item => item.ReconciliationNote).HasMaxLength(1024);
            entity.Property(item => item.ReconciliationSource).HasMaxLength(64);
            entity.HasIndex(item => item.ClientExecutionId).IsUnique();
            entity.HasIndex(item => item.PositionTicket);
            entity.HasIndex(item => item.PositionIdentifier);
            entity.HasIndex(item => item.State);
            entity.HasMany(item => item.ManagementActions).WithOne(item => item.DemoExecution!).HasForeignKey(item => item.DemoExecutionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<DemoExecutionManagementAction>(entity =>
        {
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.RequestedStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.RequestedTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.ObservedBeforeStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.ObservedBeforeTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.AppliedStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.AppliedTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.BrokerRetcode).HasMaxLength(64);
            entity.Property(item => item.BrokerMessage).HasMaxLength(1024);
            entity.Property(item => item.ReconciliationNote).HasMaxLength(1024);
            entity.Property(item => item.ReconciliationSource).HasMaxLength(64);
            entity.HasIndex(item => item.ClientManagementActionId).IsUnique();
            entity.HasIndex(item => new { item.DemoExecutionId, item.State });
        });
        builder.Entity<DemoStrategySession>(entity =>
        {
            entity.Property(session => session.Interval).HasMaxLength(8);
            entity.Property(session => session.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(session => session.FailureMessage).HasMaxLength(1024);
            entity.Property(session => session.NewEntriesPaused).HasDefaultValue(false);
            entity.Property(session => session.FixedLots).HasPrecision(18, 8);
            entity.Property(session => session.RiskReward).HasPrecision(18, 8);
            entity.Property(session => session.MinEmaGapPercent).HasPrecision(8, 4);
            entity.Property(session => session.MaxStopDistancePercent).HasPrecision(8, 4);
            entity.Property(session => session.MaxReentryAgeBars).HasDefaultValue(6);
            entity.HasIndex(session => session.Status);
            entity.HasMany(session => session.Symbols).WithOne(symbol => symbol.DemoStrategySession!).HasForeignKey(symbol => symbol.DemoStrategySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.Intents).WithOne(intent => intent.DemoStrategySession!).HasForeignKey(intent => intent.DemoStrategySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.PositionManagement).WithOne(item => item.DemoStrategySession!).HasForeignKey(item => item.DemoStrategySessionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<DemoStrategySessionSymbol>(entity =>
        {
            entity.Property(symbol => symbol.Symbol).HasMaxLength(32);
            entity.Property(symbol => symbol.BrokerSymbol).HasMaxLength(64).UseCollation("utf8mb4_bin");
            entity.HasIndex(symbol => new { symbol.DemoStrategySessionId, symbol.Symbol }).IsUnique();
            entity.Property(symbol => symbol.TrendRegimeDirection).HasConversion<string>().HasMaxLength(8);
            entity.Property(symbol => symbol.ReentryReason).HasMaxLength(1024);
            entity.HasIndex(symbol => symbol.ReentrySourceDemoExecutionId);
            entity.HasOne(symbol => symbol.ReentrySourceDemoExecution).WithMany().HasForeignKey(symbol => symbol.ReentrySourceDemoExecutionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(symbol => symbol.Intents).WithOne(intent => intent.DemoStrategySessionSymbol!).HasForeignKey(intent => intent.DemoStrategySessionSymbolId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<DemoStrategyIntent>(entity =>
        {
            entity.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(intent => intent.Direction).HasConversion<string>().HasMaxLength(8);
            entity.Property(intent => intent.SignalOpen).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalClose).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalEma9).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalEma15).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalEma100).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalGapPercent).HasPrecision(18, 8);
            entity.Property(intent => intent.SignalGapState).HasConversion<string>().HasMaxLength(16);
            entity.Property(intent => intent.StructuralStopLoss).HasPrecision(18, 8);
            entity.Property(intent => intent.StopSourceType).HasConversion<string>().HasMaxLength(32);
            entity.Property(intent => intent.IntendedTakeProfit).HasPrecision(18, 8);
            entity.Property(intent => intent.IntendedVolumeLots).HasPrecision(18, 8);
            entity.Property(intent => intent.Reason).HasMaxLength(1024);
            entity.HasIndex(intent => intent.ReentrySourceDemoExecutionId).IsUnique();
            entity.HasIndex(intent => intent.ClientExecutionId).IsUnique();
            entity.HasIndex(intent => new { intent.DemoStrategySessionId, intent.DemoStrategySessionSymbolId, intent.SignalTimeUtc, intent.Direction }).IsUnique();
            entity.HasIndex(intent => new { intent.DemoStrategySessionSymbolId, intent.Status });
            entity.HasOne(intent => intent.DemoExecution).WithMany().HasForeignKey(intent => intent.DemoExecutionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(intent => intent.ReentrySourceDemoExecution).WithMany().HasForeignKey(intent => intent.ReentrySourceDemoExecutionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DemoStrategyPositionManagement>(entity =>
        {
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(48);
            entity.Property(item => item.TakeProfitExtensionState).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.OppositeCloseState).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.OppositeSignalDirection).HasConversion<string>().HasMaxLength(8);
            entity.Property(item => item.OriginalEntryPrice).HasPrecision(18, 8);
            entity.Property(item => item.OriginalStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.OriginalTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.BestFavorablePrice).HasPrecision(18, 8);
            entity.Property(item => item.BestFavorableProgressPercent).HasPrecision(18, 8);
            entity.Property(item => item.HighestAttemptedLockPercent).HasPrecision(8, 4);
            entity.Property(item => item.HighestAppliedLockPercent).HasPrecision(8, 4);
            entity.Property(item => item.PendingProtectionLockPercent).HasPrecision(8, 4);
            entity.Property(item => item.PendingDesiredStopLoss).HasPrecision(18, 8);
            entity.Property(item => item.PendingDesiredTakeProfit).HasPrecision(18, 8);
            entity.Property(item => item.LastReason).HasMaxLength(1024);
            entity.HasIndex(item => item.DemoExecutionId).IsUnique();
            entity.HasIndex(item => new { item.DemoStrategySessionId, item.State });
            entity.HasOne(item => item.DemoStrategySessionSymbol).WithMany().HasForeignKey(item => item.DemoStrategySessionSymbolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.DemoStrategyIntent).WithMany().HasForeignKey(item => item.DemoStrategyIntentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.DemoExecution).WithMany().HasForeignKey(item => item.DemoExecutionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<StrategyOptimizationRun>(entity =>
        {
            entity.Property(run => run.MarketDataSource).HasConversion<string>().HasMaxLength(32).HasDefaultValue(MarketDataSource.LegacyBinance);
            entity.Property(run => run.SymbolsJson).HasColumnType("longtext"); entity.Property(run => run.TimeframesJson).HasColumnType("longtext"); entity.Property(run => run.GridJson).HasColumnType("longtext"); entity.Property(run => run.BaselineSettingsJson).HasColumnType("longtext");
            entity.HasIndex(run => run.Status); entity.HasMany(run => run.Candidates).WithOne(candidate => candidate.StrategyOptimizationRun!).HasForeignKey(candidate => candidate.StrategyOptimizationRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(run => run.Trades).WithOne().HasForeignKey(trade => trade.StrategyOptimizationRunId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StrategyOptimizationCandidate>(entity =>
        {
            entity.HasIndex(candidate => new { candidate.StrategyOptimizationRunId, candidate.RobustRank });
            entity.Property(candidate => candidate.Full).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
            entity.Property(candidate => candidate.Development).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
            entity.Property(candidate => candidate.Validation).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
            entity.HasMany(candidate => candidate.MarketResults).WithOne(result => result.StrategyOptimizationCandidate!).HasForeignKey(result => result.StrategyOptimizationCandidateId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StrategyOptimizationMarketResult>(entity =>
        {
            entity.HasIndex(result => new { result.StrategyOptimizationCandidateId, result.Symbol, result.Timeframe }).IsUnique();
            entity.Property(result => result.Symbol).HasMaxLength(64); entity.Property(result => result.Timeframe).HasMaxLength(8);
            entity.Property(result => result.Full).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
            entity.Property(result => result.Development).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
            entity.Property(result => result.Validation).HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<OptimizationMetrics>(value, (JsonSerializerOptions?)null) ?? new()).HasColumnType("longtext");
        });
        builder.Entity<StrategyOptimizationTrade>(entity => { entity.Property(trade => trade.Symbol).HasMaxLength(64); entity.Property(trade => trade.Timeframe).HasMaxLength(8); entity.HasIndex(trade => new { trade.StrategyOptimizationRunId, trade.StrategyOptimizationCandidateId }); });
    }
}
