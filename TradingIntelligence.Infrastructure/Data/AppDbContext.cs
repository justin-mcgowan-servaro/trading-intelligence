using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TradingIntelligence.Core.Entities;

namespace TradingIntelligence.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }
    public DbSet<Ticker> Tickers => Set<Ticker>();
    public DbSet<SignalEvent> SignalEvents => Set<SignalEvent>();
    public DbSet<MomentumScore> MomentumScores => Set<MomentumScore>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ticker
        modelBuilder.Entity<Ticker>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Symbol).IsUnique();
            e.Property(x => x.Symbol).HasMaxLength(10).IsRequired();
        });

        // SignalEvent
        modelBuilder.Entity<SignalEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TickerSymbol, x.DetectedAt });
            e.Property(x => x.SignalScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.SentimentScore).HasColumnType("decimal(5,4)");
        });

        // MomentumScore
        modelBuilder.Entity<MomentumScore>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TickerSymbol, x.ScoredAt });
            e.Property(x => x.TotalScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.RedditScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.NewsScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.VolumeScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.OptionsScore).HasColumnType("decimal(5,2)");
            e.Property(x => x.SentimentScore).HasColumnType("decimal(5,2)");
        });

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
        });

        // Watchlist
        modelBuilder.Entity<Watchlist>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.TickerSymbol }).IsUnique();
            e.HasOne(x => x.User)
             .WithMany(u => u.Watchlists)
             .HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<OtpCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.ExpiresAt); // For cleanup queries
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
        });

        /// Seed the ticker validation list with common US stocks
        modelBuilder.Entity<Ticker>().HasData(
            new Ticker { Id = 1, Symbol = "AAPL", CompanyName = "Apple Inc", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 2, Symbol = "NVDA", CompanyName = "NVIDIA Corporation", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 3, Symbol = "MSFT", CompanyName = "Microsoft Corporation", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 4, Symbol = "TSLA", CompanyName = "Tesla Inc", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 5, Symbol = "AMZN", CompanyName = "Amazon.com Inc", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 6, Symbol = "META", CompanyName = "Meta Platforms Inc", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 7, Symbol = "GOOGL", CompanyName = "Alphabet Inc", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 8, Symbol = "AMD", CompanyName = "Advanced Micro Devices", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 9, Symbol = "SPY", CompanyName = "SPDR S&P 500 ETF", Exchange = "NYSE", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Ticker { Id = 10, Symbol = "QQQ", CompanyName = "Invesco QQQ Trust", Exchange = "NASDAQ", IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
