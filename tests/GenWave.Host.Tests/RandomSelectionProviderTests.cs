using LoudnessMeasurement = GenWave.Core.Domain.Loudness;
using GenWave.Core.Domain;
using GenWave.Host.Selection;

namespace GenWave.Host.Tests;

/// <summary>
/// The v1 selection body (PRD §12): narrows a random ready <see cref="MediaReference"/> to a
/// <see cref="MediaItem"/> and forwards the recently-aired ids so "random" can avoid repeats.
/// </summary>
public class RandomSelectionProviderTests
{
    static MediaReference Ready(string id) =>
        new(id, $"/media/{id}.flac", $"title-{id}", new LoudnessMeasurement(-14.0, -1.5, Measurable: true),
            DurationMs: 200_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Artist: "a", Album: "b", Genre: "g", Year: 2020);

    [Fact]
    public async Task RandomProvider_ExcludesRecentIds()
    {
        var catalog = new FakeMediaCatalog(Ready("m1"));
        var provider = new RandomSelectionProvider(catalog, new FakeStationScopeProvider(new LibraryScope([1L])));

        var item = await provider.GetNextAsync(new PlayoutContext(["x", "y"]), CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal("m1", item!.MediaId);
        Assert.Equal("/media/m1.flac", item.Locator);             // narrowed from the reference
        Assert.Equal(["x", "y"], Assert.Single(catalog.RandomCalls));  // recent ids passed through
    }

    [Fact]
    public async Task RandomProvider_NoneReady_ReturnsNull()
    {
        var provider = new RandomSelectionProvider(
            new FakeMediaCatalog(ready: null), new FakeStationScopeProvider(new LibraryScope([1L])));

        var item = await provider.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

        Assert.Null(item);   // empty/cold library => null, not throw
    }
}
