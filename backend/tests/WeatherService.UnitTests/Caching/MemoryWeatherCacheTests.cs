using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using WeatherService.Application.Model;
using WeatherService.Infrastructure.Caching;

namespace WeatherService.UnitTests.Caching;

/// <summary>
/// The cache adapter has one job the in-process cache cannot do on its own:
/// keep an entry readable after it stops being fresh, so the use case can serve
/// it during an outage. These tests pin that behaviour down.
/// </summary>
public class MemoryWeatherCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly MemoryCache _memory = new(new MemoryCacheOptions());

    private MemoryWeatherCache CreateSut() => new(_memory, _clock);

    [Fact]
    public void Returns_null_for_a_key_that_was_never_written()
    {
        CreateSut().Get<Payload>("missing").Should().BeNull();
    }

    [Fact]
    public void Returns_a_fresh_entry_right_after_it_is_written()
    {
        var sut = CreateSut();
        sut.Set("k", new Payload("v"), FreshFor, KeepFor);

        var entry = sut.Get<Payload>("k");

        entry.Should().NotBeNull();
        entry!.Value.Content.Should().Be("v");
        entry.IsFresh(_clock.GetUtcNow()).Should().BeTrue();
        entry.StoredAt.Should().Be(Now);
    }

    [Fact]
    public void Keeps_serving_an_entry_that_is_no_longer_fresh()
    {
        // This is the whole point of the adapter: expired but still readable.
        var sut = CreateSut();
        sut.Set("k", new Payload("v"), FreshFor, KeepFor);

        _clock.Advance(FreshFor + TimeSpan.FromMinutes(1));
        var entry = sut.Get<Payload>("k");

        entry.Should().NotBeNull();
        entry!.IsFresh(_clock.GetUtcNow()).Should().BeFalse();
        entry.Value.Content.Should().Be("v");
    }

    [Fact]
    public void Drops_an_entry_once_it_is_past_its_retention_window()
    {
        var sut = CreateSut();
        sut.Set("k", new Payload("v"), FreshFor, KeepFor);

        _clock.Advance(KeepFor + TimeSpan.FromMinutes(1));

        sut.Get<Payload>("k").Should().BeNull();
    }

    [Fact]
    public void Overwrites_a_previous_entry_under_the_same_key()
    {
        var sut = CreateSut();
        sut.Set("k", new Payload("old"), FreshFor, KeepFor);
        _clock.Advance(TimeSpan.FromMinutes(1));
        sut.Set("k", new Payload("new"), FreshFor, KeepFor);

        sut.Get<Payload>("k")!.Value.Content.Should().Be("new");
    }

    [Fact]
    public void Does_not_confuse_entries_stored_under_different_types()
    {
        var sut = CreateSut();
        sut.Set("k", new Payload("v"), FreshFor, KeepFor);

        sut.Get<OtherPayload>("k").Should().BeNull();
    }

    private sealed record Payload(string Content);

    private sealed record OtherPayload(string Content);
}
