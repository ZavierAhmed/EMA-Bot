using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Data;

internal static class GridBacktestModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        var types = new[] { typeof(GridBacktestRun), typeof(GridBacktestCycle), typeof(GridBacktestPlannedLevel),
            typeof(GridBacktestBasket), typeof(GridBacktestLeg), typeof(GridBacktestEvent), typeof(GridBacktestDiagnostic) };
        foreach (var type in types)
        {
            var entity = builder.Entity(type).ToTable(type.Name + "s");
            foreach (var property in type.GetProperties())
            {
                if (property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?))
                    entity.Property(property.Name).HasPrecision(28, 12);
                if (property.PropertyType == typeof(string))
                    entity.Property(property.Name).HasMaxLength(property.Name is "Detail" or "FailureMessage" ? 1024 : 128);
            }
        }
        builder.Entity<GridBacktestRun>().Property(r => r.Symbol).UseCollation("utf8mb4_bin");
        builder.Entity<GridBacktestRun>().HasIndex(r => r.CreatedAtUtc);
        builder.Entity<GridBacktestRun>().HasMany(r => r.Cycles).WithOne().HasForeignKey(c => c.GridBacktestRunId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestRun>().HasMany(r => r.Events).WithOne().HasForeignKey(e => e.GridBacktestRunId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestRun>().HasMany(r => r.Diagnostics).WithOne().HasForeignKey(d => d.GridBacktestRunId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestCycle>().HasIndex(c => new { c.GridBacktestRunId, c.Sequence }).IsUnique();
        builder.Entity<GridBacktestCycle>().HasMany(c => c.PlannedLevels).WithOne().HasForeignKey(l => l.GridBacktestCycleId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestCycle>().HasMany(c => c.Baskets).WithOne().HasForeignKey(b => b.GridBacktestCycleId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestBasket>().HasIndex(b => b.GridBacktestCycleId).IsUnique();
        builder.Entity<GridBacktestBasket>().HasMany(b => b.Legs).WithOne().HasForeignKey(l => l.GridBacktestBasketId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GridBacktestPlannedLevel>().HasIndex(l => new { l.GridBacktestCycleId, l.Direction, l.LevelNumber }).IsUnique();
        builder.Entity<GridBacktestLeg>().HasIndex(l => new { l.GridBacktestBasketId, l.LevelNumber }).IsUnique();
        builder.Entity<GridBacktestEvent>().HasIndex(e => new { e.GridBacktestRunId, e.Sequence }).IsUnique();
        builder.Entity<GridBacktestDiagnostic>().HasIndex(d => new { d.GridBacktestRunId, d.Sequence }).IsUnique();
    }
}
