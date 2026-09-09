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

    /// <summary>
    /// Points at a local Postgres and carries no password.
    ///
    /// A committed default is a default everybody gets, so it must not be a
    /// credential: the compose stack supplies the real one through
    /// Database__ConnectionString, and Development runs on SQLite where the
    /// question never comes up. Somewhere that needs a password says so through
    /// the environment.
    /// </summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=weather;Username=weather";

    public string SqliteConnectionString { get; set; } = "Data Source=weather.db";

    /// <summary>Apply pending migrations on startup. Convenient here, not a production habit.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
