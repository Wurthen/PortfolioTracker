using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Data;

public class PortfolioDbContext : DbContext
{
    public PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : base(options)
    {
    }

    public DbSet<PortfolioItem> PortfolioItems { get; set; } = null!;
    public DbSet<SymbolPrice> SymbolPrices { get; set; } = null!;
    public DbSet<PortfolioHistoryPoint> PortfolioHistoryPoints { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Symbol }).IsUnique();
            entity.Property(e => e.Shares).HasPrecision(18, 8);
            entity.Property(e => e.PurchasePrice).HasPrecision(18, 4);
            entity.Property(e => e.Commission).HasPrecision(18, 2);
            entity.Property(e => e.AlternativeSymbol).HasMaxLength(50);
        });

        modelBuilder.Entity<SymbolPrice>(entity =>
        {
            entity.HasKey(e => e.Symbol);
            entity.Property(e => e.Price).HasPrecision(18, 4);
        });

        modelBuilder.Entity<PortfolioHistoryPoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ItemId, e.Date }).IsUnique();
            entity.HasIndex(e => e.UserId);
        });
    }
}
