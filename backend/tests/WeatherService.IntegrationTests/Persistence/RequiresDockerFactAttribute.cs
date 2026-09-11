using Xunit;
using Xunit.Sdk;

namespace WeatherService.IntegrationTests.Persistence;

/// <summary>
/// A <see cref="FactAttribute"/> that reports itself as skipped when there is no
/// Docker daemon to talk to.
///
/// The Postgres tests need a real engine, and a real engine arrives in a
/// container. But `make test` has to stay runnable by someone who cloned this
/// with nothing but the .NET 8 SDK — the README promises exactly that. A hard
/// failure there would say "this suite is broken" when the truth is "this machine
/// has no Docker", and the two deserve different words.
///
/// xUnit v2 reads <see cref="FactAttribute.Skip"/> at DISCOVERY time, which is
/// why the probe is a static <see cref="Lazy{T}"/>: it runs once per test run
/// rather than once per test, and its answer cannot change midway.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            Skip = "No Docker daemon is reachable, so the Postgres engine cannot be started.";
        }
    }
}

/// <summary>
/// Asks Testcontainers' own runtime whether it can reach a Docker endpoint,
/// rather than guessing from a socket path. Testcontainers already resolves
/// Docker Desktop, Colima, Podman and a remote DOCKER_HOST; reimplementing that
/// here would get one of them wrong.
/// </summary>
internal static class DockerEnvironment
{
    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            return DotNet.Testcontainers.Configurations.TestcontainersSettings
                .OS.DockerEndpointAuthConfig is not null;
        }
        catch (Exception)
        {
            // Any failure resolving the endpoint means the same thing to a test
            // that needs a container: it is not going to get one.
            return false;
        }
    });

    public static bool IsAvailable => Probe.Value;
}
