using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WeatherService.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build each context without booting the API.
///
/// The connection strings below are only used to pick a provider dialect while
/// generating migrations — no connection is ever opened at design time, which is
/// why it is safe (and deliberate) that they point at throwaway local defaults.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresWeatherDbContext>
{
    public PostgresWeatherDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<PostgresWeatherDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=weather;Username=weather;Password=weather")
                .Options);
}

/// <inheritdoc cref="PostgresDesignTimeFactory"/>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteWeatherDbContext>
{
    public SqliteWeatherDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<SqliteWeatherDbContext>()
                .UseSqlite("Data Source=weather.db")
                .Options);
}
