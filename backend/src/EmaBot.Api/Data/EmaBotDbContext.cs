using EmaBot.Api.Auth;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmaUser>(entity =>
        {
            entity.Property(user => user.IsActive).HasDefaultValue(true);
        });

        builder.Entity<MonitoredSymbol>(entity =>
        {
            entity.HasIndex(symbol => symbol.Symbol).IsUnique();
            entity.Property(symbol => symbol.Symbol).HasMaxLength(32);
            entity.Property(symbol => symbol.BaseAsset).HasMaxLength(32);
            entity.Property(symbol => symbol.QuoteAsset).HasMaxLength(16);
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
        });
        builder.Entity<BacktestRun>(entity => { entity.Property(run => run.Symbol).HasMaxLength(32); entity.Property(run => run.Interval).HasMaxLength(8); entity.Property(run => run.MinEmaGapPercent).HasPrecision(8, 4); entity.Property(run => run.MaxStopDistancePercent).HasPrecision(8, 4); entity.Property(run => run.StartingBalanceUsdt).HasPrecision(18, 8); entity.Property(run => run.EndingBalanceUsdt).HasPrecision(18, 8); entity.Property(run => run.MarginPerTradePercent).HasPrecision(8, 4); entity.Property(run => run.Leverage).HasPrecision(8, 4); entity.HasMany(run => run.Trades).WithOne(trade => trade.BacktestRun!).HasForeignKey(trade => trade.BacktestRunId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<BacktestTrade>(entity => { entity.Property(trade => trade.AccountEquityAtEntryUsdt).HasPrecision(18, 8); entity.Property(trade => trade.MarginUsedUsdt).HasPrecision(18, 8); entity.Property(trade => trade.Leverage).HasPrecision(8, 4); entity.Property(trade => trade.SignalOpen).HasPrecision(18, 8); entity.HasIndex(trade => trade.BacktestRunId); entity.HasMany(trade => trade.Events).WithOne(item => item.BacktestTrade!).HasForeignKey(item => item.BacktestTradeId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<BacktestTradeEvent>(entity => entity.HasIndex(item => item.BacktestTradeId));
        builder.Entity<PaperSession>(entity =>
        {
            entity.Property(session => session.Interval).HasMaxLength(8);
            entity.Property(session => session.FeePercentPerSide).HasPrecision(8, 4);
            entity.Property(session => session.MinEmaGapPercent).HasPrecision(8, 4);
            entity.Property(session => session.MaxStopDistancePercent).HasPrecision(8, 4);
            entity.Property(session => session.StartingBalanceUsdt).HasPrecision(18, 8);
            entity.Property(session => session.CurrentBalanceUsdt).HasPrecision(18, 8);
            entity.Property(session => session.MarginPerTradePercent).HasPrecision(8, 4);
            entity.Property(session => session.Leverage).HasPrecision(8, 4);
            entity.Property(session => session.UsedMarginUsdt).HasPrecision(18, 8);
            entity.HasIndex(session => session.Status);
            entity.HasMany(session => session.Symbols).WithOne(symbol => symbol.PaperSession!).HasForeignKey(symbol => symbol.PaperSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(session => session.Trades).WithOne(trade => trade.PaperSession!).HasForeignKey(trade => trade.PaperSessionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PaperSessionSymbol>(entity =>
        {
            entity.Property(symbol => symbol.Symbol).HasMaxLength(32);
            entity.HasIndex(symbol => new { symbol.PaperSessionId, symbol.Symbol }).IsUnique();
            entity.HasMany(symbol => symbol.Trades).WithOne(trade => trade.PaperSessionSymbol!).HasForeignKey(trade => trade.PaperSessionSymbolId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaperTrade>(entity =>
        {
            entity.Property(trade => trade.Symbol).HasMaxLength(32);
            entity.Property(trade => trade.Interval).HasMaxLength(8);
            entity.Property(trade => trade.AccountEquityAtEntryUsdt).HasPrecision(18, 8);
            entity.Property(trade => trade.MarginUsedUsdt).HasPrecision(18, 8);
            entity.Property(trade => trade.Leverage).HasPrecision(8, 4);
            entity.Property(trade => trade.SignalOpen).HasPrecision(18, 8);
            entity.HasIndex(trade => new { trade.PaperSessionSymbolId, trade.Status });
            entity.HasMany(trade => trade.Events).WithOne(item => item.PaperTrade!).HasForeignKey(item => item.PaperTradeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
