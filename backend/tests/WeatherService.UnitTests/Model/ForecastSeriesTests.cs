using FluentAssertions;
using WeatherService.Application.Model;

namespace WeatherService.UnitTests.Model;

/// <summary>
/// The projection that turns one stored horizon into whatever range a caller
/// asked for.
///
/// It is a pure function over a list and a clock, and it is where the only
/// genuinely subtle rule in this feature lives: which days count as past.
/// </summary>
public class ForecastSeriesTests
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly DateTimeOffset Noon = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Takes_the_requested_number_of_days_from_the_front()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 9, 10), length: 16);

        var upcoming = series.Upcoming(7, Noon);

        upcoming.Days.Should().HaveCount(7);
        upcoming.Days[0].Date.Should().Be(new DateOnly(2026, 9, 10));
        upcoming.Days[6].Date.Should().Be(new DateOnly(2026, 9, 16));
    }

    [Fact]
    public void Carries_the_location_and_the_retrieval_instant_through_untouched()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 9, 10), length: 16);

        var upcoming = series.Upcoming(3, Noon);

        upcoming.Location.Should().Be(SanSalvador);
        upcoming.RetrievedAt.Should().Be(series.RetrievedAt);
    }

    /// <summary>
    /// A stored series outlives some of its own days. Counting from index zero
    /// would hand a caller days that have already happened, dressed up as a
    /// forecast — which is the failure a degraded answer is supposed to avoid,
    /// not commit.
    /// </summary>
    [Fact]
    public void Leaves_out_days_that_have_certainly_already_happened()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 9, 4), length: 16);

        var upcoming = series.Upcoming(7, Noon);

        upcoming.Days.Should().HaveCount(7);
        upcoming.Days[0].Date.Should().Be(new DateOnly(2026, 9, 9));
    }

    /// <summary>
    /// Open-Meteo dates are local to the forecast location; this clock is UTC.
    /// At UTC-12 the local date runs a whole day behind, so a day matching
    /// yesterday's UTC date may well be the location's today — and dropping it
    /// would leave the interface with no "today" card at all. Keeping one
    /// finished day costs a card; dropping a live one costs the answer.
    /// </summary>
    [Fact]
    public void Keeps_the_day_before_because_the_location_may_lie_west_of_utc()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 9, 9), length: 16);

        var upcoming = series.Upcoming(2, Noon);

        upcoming.Days[0].Date.Should().Be(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void Returns_every_day_that_is_left_when_fewer_remain_than_were_asked_for()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 9, 10), length: 3);

        var upcoming = series.Upcoming(7, Noon);

        upcoming.Days.Should().HaveCount(3);
    }

    [Fact]
    public void Comes_back_empty_when_every_day_it_holds_is_in_the_past()
    {
        var series = ASeriesStartingOn(new DateOnly(2026, 8, 1), length: 16);

        var upcoming = series.Upcoming(7, Noon);

        upcoming.Days.Should().BeEmpty();
    }

    private static ForecastSeries ASeriesStartingOn(DateOnly first, int length) =>
        new(
            SanSalvador,
            Enumerable
                .Range(0, length)
                .Select(offset => new DailyForecast(
                    first.AddDays(offset),
                    MinTemperatureC: 21.0,
                    MaxTemperatureC: 31.0,
                    Condition: WeatherCondition.PartlyCloudy))
                .ToArray(),
            RetrievedAt: Noon);
}
