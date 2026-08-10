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
            entity.Property(settings => settings.RiskReward).HasPrecision(18, 8);
            entity.Property(settings => settings.FixedOrderSizeUsdt).HasPrecision(18, 8);
        });
    }
}
