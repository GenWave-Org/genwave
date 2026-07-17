// STORY-101 — Artist fidelity for engine-initiated plays (WIRE) (Epic R / SPEC F29.9, gitea-#192)
//
// BDD specification — xUnit. A safe play whose row has a non-empty artist must surface it in
// /api/now-playing, /api/play-history, and the Icecast on-air metadata. R6 root-caused the drop
// across the four candidate layers named in docs/PLAN.md — the safe-track SELECT columns,
// LiquidsoapAnnotationBuilder's conditional artist, the engine's metadata export, and
// EngineMetadata.ExtractAnnotations — and found each of the four sound in isolation (a live
// diagnostic on a scratch stack confirmed the annotation carries artist end to end for a
// straightforward advance). The guilty gap is PlayoutFeeder's own consumption of
// ExtractAnnotations: it only ever ran ONCE per engine-initiated media id (guarded by
// `pushedMeta.ContainsKey`), so a transient miss on the FIRST occurrence stuck forever. The fix
// (feederOwnedIds) re-extracts on every advance onto an id the feeder never pushed itself, so a
// miss self-heals the next time that safe-rotation track comes on-air, while feeder-pushed
// entries are still never touched. Live on-air proof is R13's gate job.
//
// A genuine engine-side defect was ALSO found live on the R6 diagnostic stack (Liquidsoap's
// output metadata retains a field's previous value across a track boundary when the new track's
// annotate: omits it — a real "artist" bleed from one safe track onto the next artistless one).
// Two candidate engine-side fixes (`metadata.map(update=false, ...)` and
// `reset_last_metadata_on_track := true`) were spiked live and BOTH regressed further: on-air
// track_id detection itself froze (the engine kept genuinely alternating tracks in its logs while
// `output.icecast.metadata` never advanced past the first one) — a materially worse failure than
// the bug being fixed, on the one path that must never go stale. Reverted; recorded in
// docs/MEMORY.md as a follow-up needing a live-stack Liquidsoap expert pass, not a WIRE-task risk.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Host.Playout;

using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtistFidelityEngineInitiatedPlays
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnnotationCarriesArtist
    {
        [Fact]
        public async Task SafeTrackResponseStampsArtistWhenTheRowHasOne()
        {
            // GET /internal/safe-track with a fake catalog row (artist = "Test Station") →
            // the annotate line carries artist="Test Station".
            var track = new MediaReference(
                MediaId: "seed-1",
                Locator: "/authored/seed.wav",
                Title: "Please Stand By",
                Loudness: new CoreLoudness(-16.0, -1.0, Measurable: true),
                DurationMs: 4_000,
                SampleRate: 44100,
                Channels: 2,
                BitrateKbps: 192,
                Artist: "Test Station",
                Album: null,
                Genre: null,
                Year: null);
            var catalog = new FakeMediaCatalog(track);
            var stationOpts = new StationOptions
            {
                Id = "test-station",
                Name = "Test Station",
                Voice = "en-us",
                Scope = new StationScopeOptions { LibraryIds = [1L] },
                SafeScope = new StationScopeOptions { LibraryIds = [1L] },
            };

            var ctx = new DefaultHttpContext();
            var result = await InternalEndpoints.HandleSafeTrackAsync(
                catalog,
                new FixedOptionsMonitor<StationOptions>(stationOpts),
                new FixedOptionsMonitor<LoudnessOptions>(new LoudnessOptions()),
                NullLogger.Instance,
                ctx.Response,
                CancellationToken.None);
            var body = result is ContentHttpResult text ? text.ResponseContent ?? string.Empty : string.Empty;

            Assert.Contains("artist=\"Test Station\"", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioFeederSurfacesArtist
    {
        [Fact]
        public async Task EngineInitiatedPlayWithArtistInOutputMetadataReachesNowPlaying()
        {
            // Feeder tick sees an unrecognized track_id with artist in the parsed frame →
            // the snapshot (feeder.CurrentOnAir) carries it.
            var meta = new TrackMeta("safe-1", Title: "Please Stand By", Artist: "Test Station", ReplayGain: "-2.50 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()));

            await feeder.TickAsync(CancellationToken.None);  // boot: safe rotation (no track_id)
            await feeder.TickAsync(CancellationToken.None);  // advance to safe-1

            Assert.Equal("Test Station", feeder.CurrentOnAir?.Artist);
        }

        [Fact]
        public async Task PlayHistoryEntryCarriesTheSameArtist()
        {
            // The same advance, observed through the ring the TrackAired sink feeds — the
            // real PlayHistoryService behind a sink, exactly as PlayHistoryEventSink wires it in
            // production (gitea-#246).
            var meta = new TrackMeta("safe-2", Title: "Please Stand By", Artist: "Test Station", ReplayGain: "-2.50 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            var history = new PlayHistoryService(new FakeOptionsMonitor<AdminOptions>(new AdminOptions()));
            const string stationId = "1";
            var sink = new CapturingEventSink
            {
                OnTrackAired = t => history.Push(new PlayHistoryEntry(
                    stationId, t.MediaId, t.Title, t.Artist, t.GainDb, t.StartedAt, null, t.DurationMs)),
            };
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()), events: sink);

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            var ring = history.GetEntries(stationId);
            Assert.Equal("Test Station", Assert.Single(ring).Artist);
        }
    }

    public sealed class ScenarioLiveOnAir
    {
        // Verified on scratch stack 2026-07-11 (r6diag): GET /internal/safe-track's annotation
        // and the engine's own output.icecast.metadata telnet frame both carried
        // artist="Gap Smoke FM" (the seeded segment's station name) for a straightforward advance.
        // The residual engine-side "artist bleed across a track boundary" defect found on the
        // same stack (see file header) needs a live drain re-proof once an engine-side fix lands;
        // operator live-stack re-proof at R13.
        [Fact(Skip = "Verified on scratch stack 2026-07-11 (r6diag): safe-track annotation and " +
            "engine output metadata both carried artist=\"Gap Smoke FM\"; operator live-stack re-proof at R13")]
        public void SeededSegmentAirsWithTheStationNameAsArtist()
        {
            Assert.Fail("pending R13 live gate");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioArtistlessRowsStayLegal
    {
        [Fact]
        public async Task RowWithoutArtistSurfacesNull()
        {
            // F24.1's "else null" now scoped to genuinely artistless rows.
            var meta = new TrackMeta("safe-3", Title: "Untagged Bumper", Artist: null, ReplayGain: "0.00 dB");
            var ls = new EngineInitiatedControl([null, meta]);
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()));

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Null(feeder.CurrentOnAir?.Artist);
        }

        [Fact]
        public async Task NoNewTelnetCallsAndNoPerTickDbReads()
        {
            // The fix stays inside the existing metadata poll (F16.6/F24.2): PlayoutFeeder's
            // constructor takes only ILiquidsoapControl + INextItemProvider — no IMediaCatalog or
            // other DB-reaching collaborator was added — and re-extracting on a REPEAT advance
            // onto the same engine-initiated id (the self-heal path) issues no MORE telnet calls
            // than the shipped id-change gate already made before this fix: one OnAirNewestAsync
            // per tick, one MetadataAsync per tick that changes id (never per repeat WITHOUT an
            // id change in between).
            var ctorParamTypeNames = typeof(PlayoutFeeder)
                .GetConstructors()[0]
                .GetParameters()
                .Select(p => p.ParameterType.Name)
                .ToArray();
            Assert.DoesNotContain(ctorParamTypeNames, n => n.Contains("Catalog", StringComparison.Ordinal));

            var meta = new TrackMeta("safe-4", Title: "Please Stand By", Artist: "Test Station", ReplayGain: "-2.50 dB");
            // The same id repeats across drains: [null, meta, null, meta] — safe → safe-4 → drain → safe-4 again.
            // Every one of these 4 ticks changes id, so both calls fire on every tick — the pre-fix
            // shape exactly, since the fix only changes what happens to pushedMeta AFTER MetadataAsync
            // returns, never whether/how often OnAirNewestAsync or MetadataAsync themselves are called.
            var ls = new CountingEngineInitiatedControl([null, meta, null, meta]);
            var feeder = new PlayoutFeeder(ls, new NullProvider(), new FakeRotationSettingsProvider(new RotationSettings()));

            await feeder.TickAsync(CancellationToken.None);  // boot: safe rotation
            await feeder.TickAsync(CancellationToken.None);  // advance to safe-4 (1st time)
            await feeder.TickAsync(CancellationToken.None);  // drain
            await feeder.TickAsync(CancellationToken.None);  // advance to safe-4 (2nd time — self-heal path)

            Assert.Equal(4, ls.OnAirNewestCalls);
            Assert.Equal(4, ls.MetadataCalls);
        }
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scripts on-air metadata for engine-initiated play scenarios. Null entries simulate the safe
    /// rotation (no track_id — feeder sees a drain token); <see cref="TrackMeta"/> entries simulate
    /// a track the engine fetched itself (track_id present in output metadata, but feeder never
    /// called PushAsync for it). Mirrors Story066's <c>EngineInitiatedControl</c> — kept local to
    /// this spec file rather than shared, matching the project's per-spec-fake convention.
    /// </summary>
    sealed class EngineInitiatedControl(IEnumerable<TrackMeta?> sequence) : ILiquidsoapControl
    {
        const string SafeToken = "__safe__";

        readonly Queue<TrackMeta?> queue = new(sequence);
        TrackMeta? current;
        bool started;

        public Task<string?> OnAirNewestAsync(CancellationToken ct)
        {
            if (queue.Count > 0)
            {
                current = queue.Dequeue();
                started = true;
            }
            if (!started) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(current?.TrackId ?? SafeToken);
        }

        public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (current is not null)
            {
                map["track_id"] = current.TrackId;
                if (current.Title is not null) map["title"] = current.Title;
                if (current.Artist is not null) map["artist"] = current.Artist;
                if (current.ReplayGain is not null) map["replay_gain"] = current.ReplayGain;
            }
            return Task.FromResult(new EngineMetadata(map));
        }

        public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
            => Task.FromResult(item.MediaId);
    }

    /// <summary>
    /// <see cref="EngineInitiatedControl"/> variant that counts calls, for the
    /// "no new telnet calls" fact — asserts the fix rides the existing poll shape rather than
    /// adding a call per repeat occurrence of an engine-initiated id.
    /// </summary>
    sealed class CountingEngineInitiatedControl(IEnumerable<TrackMeta?> sequence) : ILiquidsoapControl
    {
        const string SafeToken = "__safe__";

        readonly Queue<TrackMeta?> queue = new(sequence);
        TrackMeta? current;
        bool started;

        public int OnAirNewestCalls { get; private set; }
        public int MetadataCalls { get; private set; }

        public Task<string?> OnAirNewestAsync(CancellationToken ct)
        {
            OnAirNewestCalls++;
            if (queue.Count > 0)
            {
                current = queue.Dequeue();
                started = true;
            }
            if (!started) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(current?.TrackId ?? SafeToken);
        }

        public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
        {
            MetadataCalls++;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (current is not null)
            {
                map["track_id"] = current.TrackId;
                if (current.Title is not null) map["title"] = current.Title;
                if (current.Artist is not null) map["artist"] = current.Artist;
                if (current.ReplayGain is not null) map["replay_gain"] = current.ReplayGain;
            }
            return Task.FromResult(new EngineMetadata(map));
        }

        public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
            => Task.FromResult(item.MediaId);
    }

    /// <summary>Yields nothing — used when the feeder should not push any tracks.</summary>
    sealed class NullProvider : INextItemProvider
    {
        public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
            => Task.FromResult<MediaItem?>(null);
    }

    /// <summary>Scripts one engine-initiated track with all its metadata fields.</summary>
    sealed record TrackMeta(string TrackId, string? Title, string? Artist, string? ReplayGain);

    /// <summary>Minimal fixed-value <see cref="IOptionsMonitor{T}"/> — mirrors Story056's FakeOptionsMonitor.</summary>
    sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
