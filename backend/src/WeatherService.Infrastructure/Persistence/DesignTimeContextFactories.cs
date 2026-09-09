using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WeatherService.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build each context without booting the API.
///
/// Generating a migration opens no connection — the string only picks a
/// provider dialect. Applying one does, which is why this reads the same
/// environment the compose stack is configured from rather than carrying a
/// password of its own: `make migration-up` then works off the .env that is
/// already there, and nothing secret is committed to say so.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresWeatherDbContext>
{
    public PostgresWeatherDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<PostgresWeatherDbContext>()
                .UseNpgsql(PostgresConnectionString())
                .Options);

    /// <summary>
    /// A whole connection string wins if one is set; otherwise it is assembled
    /// from the POSTGRES_* variables docker compose already reads. With neither,
    /// it falls back to a local server and no credential at all.
    /// </summary>
    private static string PostgresConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("Database__ConnectionString");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var host = FromEnvironment("POSTGRES_HOST", "localhost");
        var port = FromEnvironment("POSTGRES_PORT", "5432");
        var database = FromEnvironment("POSTGRES_DB", "weather");
        var user = FromEnvironment("POSTGRES_USER", "weather");
        var secret = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        var credential = string.IsNullOrWhiteSpace(secret) ? string.Empty : $";Password={secret}";

        return $"Host={host};Port={port};Database={database};Username={user}{credential}";
    }

    private static string FromEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
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
