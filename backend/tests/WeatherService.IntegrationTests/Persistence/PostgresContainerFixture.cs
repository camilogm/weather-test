using Testcontainers.PostgreSql;

namespace WeatherService.IntegrationTests.Persistence;

/// <summary>
/// One Postgres container for the whole test class, started once.
///
/// Starting a container costs a couple of seconds, which is fine once and
/// ruinous per test — xUnit builds a fresh instance of a test class for every
/// method, so anything held there would be paid for eleven times over. A class
/// fixture is built once and shared, and the per-test isolation comes from a
/// fresh DATABASE inside this one server instead, which costs milliseconds.
///
/// The image is pinned. "postgres:latest" in a test suite means the suite can
/// start failing on a day nobody touched it, and the version here tracks the one
/// docker-compose.yml runs so the tests speak for the deployment.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("weather_template")
        .WithUsername("weather_test")
        .WithPassword("weather_test")
        // Without this the first connection can arrive while Postgres is still
        // replaying its bootstrap WAL and be refused.
        .WithCleanUp(true)
        .Build();

    /// <summary>Null when Docker is unreachable, in which case every test is skipped.</summary>
    public string? AdminConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            return;
        }

        await _container.StartAsync();
        AdminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (AdminConnectionString is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
