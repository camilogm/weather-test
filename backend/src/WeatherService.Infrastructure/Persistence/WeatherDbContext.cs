using Microsoft.EntityFrameworkCore;

namespace WeatherService.Infrastructure.Persistence;

/// <summary>
/// The shared model. Concrete contexts exist per provider so each can own its
/// own migration history — Postgres and SQLite do not agree on column types, and
/// pretending one set of migrations fits both is how a fallback database breaks
/// on first run.
/// </summary>
public abstract class WeatherDbContext : DbContext
{
    protected WeatherDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<ForecastSnapshot> Forecasts => Set<ForecastSnapshot>();

    public DbSet<ForecastDay> ForecastDays => Set<ForecastDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ForecastSnapshot>(snapshot =>
        {
            snapshot.ToTable("forecast_snapshots");
            snapshot.HasKey(s => s.Id);

            snapshot.Property(s => s.LocationKey).HasMaxLength(32).IsRequired();
            snapshot.Property(s => s.LocationName).HasMaxLength(128).IsRequired();
            snapshot.Property(s => s.RetrievedAtUtc).IsRequired();

            // Every read is "latest snapshot for this location", so that is
            // exactly what the index is shaped for.
            snapshot
                .HasIndex(s => new { s.LocationKey, s.RetrievedAtUtc })
                .HasDatabaseName("ix_forecast_snapshots_location_key_retrieved_at");

            snapshot
                .HasMany(s => s.Days)
                .WithOne()
                .HasForeignKey(d => d.ForecastSnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForecastDay>(day =>
        {
            day.ToTable("forecast_days");
            day.HasKey(d => d.Id);

            day.Property(d => d.Date).IsRequired();
            day.Property(d => d.MinTemperatureC).IsRequired();
            day.Property(d => d.MaxTemperatureC).IsRequired();

            // Stored as text rather than an ordinal: a reviewer reading the table
            // should see "PartlyCloudy", and reordering the enum must not silently
            // rewrite the meaning of rows already on disk.
            day.Property(d => d.Condition).HasConversion<string>().HasMaxLength(32).IsRequired();

            day.HasIndex(d => new { d.ForecastSnapshotId, d.Date }).IsUnique();
        });
    }
}

/// <summary>Production context. Migrations live under Migrations/Postgres.</summary>
public sealed class PostgresWeatherDbContext : WeatherDbContext
{
    public PostgresWeatherDbContext(DbContextOptions<PostgresWeatherDbContext> options)
        : base(options)
    {
    }
}

/// <summary>
/// Fallback context for running without Docker, and the engine the integration
/// tests use. Migrations live under Migrations/Sqlite.
/// </summary>
public sealed class SqliteWeatherDbContext : WeatherDbContext
{
    public SqliteWeatherDbContext(DbContextOptions<SqliteWeatherDbContext> options)
        : base(options)
    {
    }
}
