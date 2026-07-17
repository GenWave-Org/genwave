// STORY-066 — Now-playing tells the truth for engine-initiated plays
//
// BDD specification — xUnit. A track the feeder did NOT push (safe-rotation plays the engine
// pulled itself) must surface title + gainDb in now-playing/play-history, parsed from the
// output metadata the feeder already polls (F24.1) — no extra telnet, no per-tick DB read
// (F16.6). artist rides along when the metadata carries it; missing/unparseable fields degrade
// to null/0 without killing the tick (F7.4).
//
// Root cause (recorded 2026-07-02, MEMORY.md):
//   Safe-rotation tracks are fetched by Liquidsoap's request.dynamic and are never pushed via
//   PlayoutFeeder.PushAsync. pushedMeta therefore has no entry → title=null, gainDb=0.
//   The fix: on an advance not in pushedMeta, call EngineMetadata.ExtractAnnotations() on the
//   already-polled output metadata dict and cache the result in pushedMeta.
//
// The unit-testable scenarios (ScenarioEngineInitiatedEntryFidelity + ScenarioDegradedMetadata)
// are un-pinned here against the real seam. The FLAC+mp3 drain proof is operator-gated.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;

using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureNowPlayingEngineInitiatedFidelity
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — engine-initiated entries carry the annotation's fields
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineInitiatedEntryFidelity
    {
        // Arrange: an engine-initiated track is on-air (track_id present in output metadata but
        // the feeder never pushed it). The metadata carries title, artist, and replay_gain.
        // Act: two ticks (boot at "safe", then advance to engine track).
        // Assert: the published TrackAired event and CurrentOnAir both carry the extracted metadata.

        [Fact]
        public async Task AnUnrecognizedTrackIdEntryCarriesTheAnnotationsTitle()
        {
            var meta = new TrackMeta("engine-1", Title: "Paranoid", Artist: "Black Sabbath", ReplayGain: "-2.50 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            string? capturedTitle = null;
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedTitle = t.Title;

            await feeder.TickAsync(CancellationToken.None);  // boot: "safe" (no track_id)
            await feeder.TickAsync(CancellationToken.None);  // advance to engine-1

            Assert.Equal("Paranoid", capturedTitle);
        }

        [Fact]
        public async Task AnUnrecognizedTrackIdEntryCarriesTheParsedReplayGainAsGainDb()
        {
            var meta = new TrackMeta("engine-1", Title: "Paranoid", Artist: "Black Sabbath", ReplayGain: "-2.50 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            double capturedGainDb = 99.0;
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedGainDb = t.GainDb;

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Equal(-2.50, capturedGainDb, precision: 10);
        }

        [Fact]
        public async Task ArtistIsPopulatedWhenTheMetadataCarriesIt()
        {
            var meta = new TrackMeta("engine-2", Title: "War Pigs", Artist: "Black Sabbath", ReplayGain: "-1.00 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            string? capturedArtist = null;
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedArtist = t.Artist;

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Equal("Black Sabbath", capturedArtist);
        }

        [Fact]
        public async Task ArtistIsNullWhenTheMetadataOmitsIt()
        {
            var meta = new TrackMeta("engine-3", Title: "Untitled", Artist: null, ReplayGain: "-1.00 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            string? capturedArtist = "not-null";
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedArtist = t.Artist;

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Null(capturedArtist);
        }

        [Fact]
        public async Task AFeederPushedEntryIsUnchangedFromTheShippedBehavior()
        {
            // A track pushed by the feeder via PushAsync should carry the MediaItem's title/gainDb
            // from pushedMeta — the ExtractAnnotations fallback must not overwrite feeder-pushed entries.
            const string trackId = "pushed-1";
            var trackInMetadata = new TrackMeta(trackId, "Wrong Title From Engine", "Wrong Artist", "-99.00 dB");
            var ls = new EngineInitiatedControl([null, trackInMetadata]);
            var item = new MediaItem(trackId, "/media/pushed.mp3", "Correct Title From Feeder",
                new CoreLoudness(-16.0, -1.0, Measurable: true), "Correct Artist");

            string? capturedTitle = null;
            string? capturedArtist = null;

            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new SingleItemProvider(item), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t =>
            {
                capturedTitle = t.Title;
                capturedArtist = t.Artist;
            };

            // Tick 1: sees "safe" → drain → pushes item (stored in pushedMeta).
            await feeder.TickAsync(CancellationToken.None);
            // Tick 2: sees "pushed-1" on-air → advance → TrackAired publishes.
            await feeder.TickAsync(CancellationToken.None);

            // pushedMeta has the entry from PushAsync; ExtractAnnotations must NOT override it.
            Assert.Equal("Correct Title From Feeder", capturedTitle);
            Assert.Equal("Correct Artist", capturedArtist);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — degraded metadata never kills the tick
    // ---------------------------------------------------------------------

    public sealed class ScenarioDegradedMetadata
    {
        [Fact]
        public async Task AMissingTitleDegradesToNullAndTheTickCompletes()
        {
            // No "title" field in the metadata dict.
            var meta = new TrackMeta("engine-4", Title: null, Artist: null, ReplayGain: "-1.00 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            string? capturedTitle = "should-be-cleared";
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedTitle = t.Title;

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);  // must not throw

            Assert.Null(capturedTitle);
        }

        [Fact]
        public async Task AnUnparseableReplayGainDegradesToZeroAndTheTickCompletes()
        {
            // replay_gain field is present but not parseable as a number.
            var meta = new TrackMeta("engine-5", Title: "Some Track", Artist: null, ReplayGain: "not-a-number dB");
            var ls = new EngineInitiatedControl([null, meta]);
            double capturedGainDb = 99.0;
            var sink = new CapturingEventSink();
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);
            sink.OnTrackAired = t => capturedGainDb = t.GainDb;

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);  // must not throw

            Assert.Equal(0.0, capturedGainDb);
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — FLAC and mp3 proven on the live stack (operator-gated)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioLiveDrainFidelity
    {
        const string Skip = "Live stack + operator: M7 wire acceptance (drain airing one FLAC and one mp3 safe track).";

        [Fact(Skip = Skip)]
        public void AFlacSafePlaySurfacesTitleAndGainInNowPlaying() { }

        [Fact(Skip = Skip)]
        public void AnMp3SafePlaySurfacesTitleAndGainInNowPlaying() { }

        [Fact(Skip = Skip)]
        public void TheRootCauseOfTheFlacMp3DiscrepancyIsRecordedInMemoryMd() { }
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scripts on-air metadata for engine-initiated play scenarios. Null entries simulate the safe
    /// rotation (no track_id — feeder sees a drain token); <see cref="TrackMeta"/> entries simulate
    /// a track the engine fetched itself (track_id present in output metadata, but feeder never
    /// called PushAsync for it).
    ///
    /// Mirrors the production split: <c>OnAirNewestAsync</c> returns a stable non-null token for
    /// safe-rotation (matching <see cref="LiquidsoapControl.DrainToken"/>'s contract), and
    /// <c>MetadataAsync</c> ignores <c>rid</c> and returns the current output metadata snapshot —
    /// exactly what the real <c>LiquidsoapControl</c> does (it re-reads the output metadata dict
    /// rather than looking up by request id).
    /// </summary>
    sealed class EngineInitiatedControl(IEnumerable<TrackMeta?> sequence) : ILiquidsoapControl
    {
        // A stable token distinct from any real track_id (numerics) or null (cold start).
        const string SafeToken = "__safe__";

        readonly Queue<TrackMeta?> queue = new(sequence);
        TrackMeta? current;   // null = safe rotation is on-air
        bool started;         // false until first dequeue (cold start → feeder returns early)

        public Task<string?> OnAirNewestAsync(CancellationToken ct)
        {
            if (queue.Count > 0)
            {
                current = queue.Dequeue();
                started = true;
            }
            if (!started) return Task.FromResult<string?>(null);  // cold start — feeder skips tick
            return Task.FromResult<string?>(current?.TrackId ?? SafeToken);
        }

        public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
        {
            // Mirrors the real implementation: ignore rid, return the current output snapshot.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (current is not null)
            {
                map["track_id"] = current.TrackId;
                if (current.Title is not null) map["title"] = current.Title;
                if (current.Artist is not null) map["artist"] = current.Artist;
                if (current.ReplayGain is not null) map["replay_gain"] = current.ReplayGain;
            }
            // current == null → safe rotation → no track_id in map → feeder reads as drain
            return Task.FromResult(new EngineMetadata(map));
        }

        public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
            => Task.FromResult(item.MediaId);
    }

    /// <summary>
    /// Variant of <see cref="EngineInitiatedControl"/> that also simulates a feeder-pushed track
    /// coming on-air after being pushed (for the "unchanged feeder-pushed entry" scenario).
    /// </summary>
    sealed class SingleItemProvider(MediaItem item) : INextItemProvider
    {
        bool yielded;

        public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
        {
            if (yielded) return Task.FromResult<MediaItem?>(null);
            yielded = true;
            return Task.FromResult<MediaItem?>(item);
        }
    }

    /// <summary>Yields nothing — used when the feeder should not push any tracks.</summary>
    sealed class NullProvider : INextItemProvider
    {
        public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
            => Task.FromResult<MediaItem?>(null);
    }

    /// <summary>Scripts one engine-initiated track with all its metadata fields.</summary>
    sealed record TrackMeta(string TrackId, string? Title, string? Artist, string? ReplayGain);
}
