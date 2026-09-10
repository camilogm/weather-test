using WeatherService.Application.Model;

namespace WeatherService.Infrastructure.Persistence;

/// <summary>
/// The persistence shape of a stored forecast.
///
/// Kept separate from <see cref="Application.Model.ForecastSeries"/> on purpose:
/// the domain model is an immutable record with no identity and no foreign keys,
/// while this one needs a primary key, mutable setters and a navigation property
/// for EF to work with. Letting EF dictate the shape of the domain is how the
/// database ends up leaking into every layer above it.
/// </summary>
public sealed class ForecastSnapshot
{
    public Guid Id { get; set; }

    /// <summary>Canonical location identity — see <see cref="GeoLocation.Key"/>.</summary>
    public string LocationKey { get; set; } = string.Empty;

    public string LocationName { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>
    /// When this snapshot was obtained from the provider, always in UTC.
    ///
    /// Stored as <see cref="DateTime"/> rather than the domain's DateTimeOffset
    /// because SQLite refuses to ORDER BY a DateTimeOffset: it persists one as
    /// text with the offset appended, which does not sort correctly. Since every
    /// read here is "the latest snapshot", an unsortable column would be useless.
    /// The offset carries no information anyway — these values are always UTC.
    /// </summary>
    public DateTime RetrievedAtUtc { get; set; }

    public List<ForecastDay> Days { get; set; } = [];
}

/// <summary>One day inside a stored snapshot.</summary>
public sealed class ForecastDay
{
    public Guid Id { get; set; }

    public Guid ForecastSnapshotId { get; set; }

    public DateOnly Date { get; set; }

    public double MinTemperatureC { get; set; }

    public double MaxTemperatureC { get; set; }

    public WeatherCondition Condition { get; set; }
}
