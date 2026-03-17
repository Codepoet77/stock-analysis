using Microsoft.EntityFrameworkCore;
using StockAnalysis.Api.Models.Strategy;

namespace StockAnalysis.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BacktestJob> BacktestJobs => Set<BacktestJob>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategyRule> StrategyRules => Set<StrategyRule>();
    public DbSet<LiveSignal> LiveSignals => Set<LiveSignal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BacktestJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Strategy>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Rules)
             .WithOne(r => r.Strategy)
             .HasForeignKey(r => r.StrategyId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StrategyRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StrategyId);
            e.Property(x => x.SignalTimeframe).HasConversion<string>();
            e.Property(x => x.BiasTimeframe).HasConversion<string>();
            e.Property(x => x.ConfirmTimeframe).HasConversion<string>();
            e.Property(x => x.ExitStrategy).HasConversion<string>();
            e.HasMany(x => x.Signals)
             .WithOne(s => s.Rule)
             .HasForeignKey(s => s.StrategyRuleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LiveSignal>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StrategyRuleId);
            e.HasIndex(x => new { x.Symbol, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.ExitStrategy).HasConversion<string>();
        });
    }
}
