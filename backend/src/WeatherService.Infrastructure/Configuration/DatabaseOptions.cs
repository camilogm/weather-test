namespace WeatherService.Infrastructure.Configuration;

public enum DatabaseProvider
{
    Postgres,
    Sqlite,
}

/// <summary>
/// Which relational engine to run against.
///
/// Postgres is the default and what docker compose brings up. SQLite exists so
/// the service can be cloned and run with nothing installed — a reviewer without
/// a working Docker should still be able to see it work.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=weather;Username=weather;Password=weather";

    public string SqliteConnectionString { get; set; } = "Data Source=weather.db";

    /// <summary>Apply pending migrations on startup. Convenient here, not a production habit.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
