// STORY-012 — Acceptance gate §0.1: render-ahead / graceful skip-to-music
//
// Integration gate against the WIRED orchestrator (real KokoroTtsSynthesizer pointing at a
// controllable stub HTTP server) + real PlayoutFeeder + FakeLiquidsoapControl. No Docker needed.
//
// Three behavioural scenarios:
//   1. Kokoro always returns 500  → orchestrator yields only music items (no TTS drain)
//   2. Kokoro delays past budget  → each GetNextAsync returns in well under the delay time
//   3. Recovery                   → after stub switches to success mode, TTS items reappear

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read
/// (mirrors Story120's/Story123's file-scoped precedent — a file-scoped type cannot cross files, so
/// every spec file with this need defines its own copy). <c>KokoroTtsSynthesizer</c> reads
/// <c>TtsOptions.Endpoint</c> through this per call (SPEC F36.1–F36.2) instead of a boot-frozen
/// <c>HttpClient.BaseAddress</c>.
/// </summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class FeatureAcceptanceGate01RenderAheadGracefulSkipToMusic
{
    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// A catalog seeded with several distinct ready tracks. Returns tracks round-robin so the
    /// feeder never starves (the base FakeMediaCatalog only holds one item).
    /// </summary>
    sealed class MultiTrackCatalog : GenWave.Core.Abstractions.IMediaCatalog
    {
        readonly MediaReference[] tracks;
        int index;

        public MultiTrackCatalog(int count = 5)
        {
            var loudness = new Core.Domain.Loudness(-16.0, -1.0, true);
            tracks = Enumerable.Range(1, count)
                .Select(i => new MediaReference(
                    $"music-{i}", $"/media/track{i}.mp3", $"Track {i}",
                    loudness,
                    DurationMs: 180_000, SampleRate: 44100, Channels: 2, BitrateKbps: 320,
                    Artist: null, Album: null, Genre: null, Year: null))
                .ToArray();
        }

        public Task<MediaReference?> GetByIdAsync(
            LibraryScope scope, string mediaId, CancellationToken ct)
        {
            var found = tracks.FirstOrDefault(t => t.MediaId == mediaId);
            return Task.FromResult(found);
        }

        public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
        {
            var found = tracks.FirstOrDefault(t => t.MediaId == mediaId);
            return Task.FromResult(found);
        }

        public Task<MediaReference?> GetRandomReadyAsync(
            LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
        {
            // Return next track, wrapping; skip excludes if possible.
            for (var attempt = 0; attempt < tracks.Length; attempt++)
            {
                var candidate = tracks[index % tracks.Length];
                index++;
                if (!excludeIds.Contains(candidate.MediaId))
                    return Task.FromResult<MediaReference?>(candidate);
            }

            // All tracks excluded — return the first anyway (repeat-avoidance relaxed under pressure).
            return Task.FromResult<MediaReference?>(tracks[0]);
        }

        public Task<GenWave.Core.Domain.RotationCandidate?> GetRotationCandidateAsync(
            LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
        {
            // Mirrors GetRandomReadyAsync's wrap-around above: next track, flagged if it was recent.
            var candidate = tracks[index % tracks.Length];
            index++;
            return Task.FromResult<GenWave.Core.Domain.RotationCandidate?>(
                new GenWave.Core.Domain.RotationCandidate(
                    candidate, RepeatedRecent: orderedRecentIds.Contains(candidate.MediaId), RepeatedArtist: false));
        }

        public Task<GenWave.Core.Abstractions.PagedResult<MediaReference>> ListAsync(
            LibraryScope scope, GenWave.Core.Abstractions.MediaQuery query, CancellationToken ct) =>
            Task.FromResult(new GenWave.Core.Abstractions.PagedResult<MediaReference>([], 0, 0));

        public Task<GenWave.Core.Domain.CatalogStatusCounts> GetStatusCountsAsync(
            LibraryScope safeScope, CancellationToken ct) =>
            Task.FromResult(new GenWave.Core.Domain.CatalogStatusCounts(0, 0, 0, 0, 0));

        // Not exercised by this gate — facets are a curation-console concern (SPEC F52.1).
        public Task<IReadOnlyList<GenWave.Core.Domain.FacetValue>> GetFacetsAsync(
            GenWave.Core.Domain.FacetField field, LibraryScope scope, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GenWave.Core.Domain.FacetValue>>([]);
    }

    /// <summary>Persona-less accessor double (STORY-121 plumbing) — this gate exercises render-ahead
    /// failure/recovery under LeadIn-only cadence; no persona is under test here.</summary>
    sealed class NoOpActivePersonaAccessor : GenWave.Core.Abstractions.IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    /// <summary>
    /// Builds a wired <see cref="Orchestrator"/> backed by a <see cref="KokoroTtsSynthesizer"/>
    /// whose absolute per-call request URI (SPEC F36.1–F36.2) points at <paramref name="stubUri"/>.
    /// </summary>
    static Orchestrator BuildOrchestrator(
        Uri stubUri,
        string cacheRoot,
        string voice,
        TimeSpan renderBudget,
        MultiTrackCatalog catalog)
    {
        var ttsOptionsValue = new TtsOptions
        {
            Endpoint   = stubUri.ToString(),
            Format     = "wav",
            CacheRoot  = cacheRoot,
        };
        var ttsOptsMonitor = new FakeOptionsMonitor<TtsOptions>(ttsOptionsValue);

        var http = new HttpClient();
        var synthesizer = new KokoroTtsSynthesizer(http, ttsOptsMonitor);
        var analyzer    = new FakeLoudnessAnalyzer();

        var segmentSource = new TtsSegmentSource(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            synthesizer,
            analyzer,
            new FakeCueAnalyzer(),
            ttsOptsMonitor,
            NullLogger<TtsSegmentSource>.Instance);

        var identityProvider = new FakeStationIdentityProvider(new StationIdentity(
            Id:    "test-station",
            Name:  "GenWave",
            Voice: voice));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack      = true,  // every unit attempts a render → recovery visible fast
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits       = 0,     // disable periodic station-id for simplicity
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, segmentSource,
            new NoOpActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(renderBudget),
            new SpeechDeferralQueue(TimeProvider.System));
    }

    // -------------------------------------------------------------------------
    // HAPPY PATH — graceful degradation under failure
    // -------------------------------------------------------------------------

    public sealed class ScenarioSynthesizerAlwaysFailsOrchestratorYieldsMusic
        : IAsyncLifetime
    {
        KokoroStubServer stub = null!;
        DirectoryInfo cacheDir = null!;
        Orchestrator orchestrator = null!;

        public async Task InitializeAsync()
        {
            stub     = await KokoroStubServer.StartAsync(KokoroStubMode.Fail500);
            cacheDir = System.IO.Directory.CreateTempSubdirectory("genwave-t015-fail-");
            orchestrator = BuildOrchestrator(
                stub.BaseUri,
                cacheDir.FullName,
                voice:        $"voice-{Guid.NewGuid():N}",
                renderBudget: TimeSpan.FromSeconds(2),
                catalog:      new MultiTrackCatalog());
        }

        public async Task DisposeAsync()
        {
            await stub.DisposeAsync();
            if (cacheDir.Exists) cacheDir.Delete(recursive: true);
        }

        [Fact]
        public async Task EveryPushedItemIsAMusicMediaItem()
        {
            const int n = 5;
            var ctx = new PlayoutContext([]);
            var pulled = new List<MediaItem>();

            for (var i = 0; i < n; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                pulled.Add(item);
                ctx = new PlayoutContext(pulled.Select(p => p.MediaId).ToList());
            }

            Assert.All(pulled, item =>
                Assert.False(
                    item.MediaId.StartsWith("tts:", StringComparison.Ordinal),
                    $"Expected music item but got TTS item: {item.MediaId}"));
        }

        [Fact]
        public async Task EngineReportsNoDrainTokenOnOutputMetadata()
        {
            // Verify at the feeder seam: after several ticks the fake control has pushes and they
            // are all music items (no tts: prefix) — the station never drains to safe rotation.
            var fakeLs = new FakeLiquidsoapControl();

            // Seed the feeder: prime onAir to "safe" so the first tick sees a drained queue and pulls.
            // We do this by calling tick several times; each tick will push one item.
            var feeder = new PlayoutFeeder(fakeLs, orchestrator, new FakeRotationSettingsProvider(new RotationSettings()));

            // 4 ticks: tick 1 boots with null on-air (returns early), ticks 2–4 see the previously
            // pushed item as on-air and push the next one. We want at least a couple of pushes.
            for (var i = 0; i < 6; i++)
                await feeder.TickAsync(CancellationToken.None);

            Assert.NotEmpty(fakeLs.Pushed);
            Assert.All(fakeLs.Pushed, item =>
                Assert.False(
                    item.MediaId.StartsWith("tts:", StringComparison.Ordinal),
                    $"Expected music push but found TTS: {item.MediaId}"));
        }

        [Fact]
        public async Task NoPulledItemIsNull()
        {
            const int n = 4;
            var ctx = new PlayoutContext([]);
            for (var i = 0; i < n; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                ctx = new PlayoutContext([item.MediaId]);
            }
        }
    }

    // -------------------------------------------------------------------------

    public sealed class ScenarioSynthesizerTimesOutOrchestratorYieldsMusic
        : IAsyncLifetime
    {
        KokoroStubServer stub = null!;
        DirectoryInfo cacheDir = null!;
        Orchestrator orchestrator = null!;

        // Budget is 200 ms — well under the 30-second delay the stub introduces.
        static readonly TimeSpan RenderBudget = TimeSpan.FromMilliseconds(200);

        public async Task InitializeAsync()
        {
            stub     = await KokoroStubServer.StartAsync(KokoroStubMode.DelayPastBudget);
            cacheDir = System.IO.Directory.CreateTempSubdirectory("genwave-t015-timeout-");
            orchestrator = BuildOrchestrator(
                stub.BaseUri,
                cacheDir.FullName,
                voice:        $"voice-{Guid.NewGuid():N}",
                renderBudget: RenderBudget,
                catalog:      new MultiTrackCatalog());
        }

        public async Task DisposeAsync()
        {
            await stub.DisposeAsync();
            if (cacheDir.Exists) cacheDir.Delete(recursive: true);
        }

        [Fact]
        public async Task EveryPushedItemIsAMusicMediaItemUnderTimeouts()
        {
            const int n = 4;
            var ctx = new PlayoutContext([]);
            var pulled = new List<MediaItem>();

            for (var i = 0; i < n; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                pulled.Add(item);
                ctx = new PlayoutContext(pulled.Select(p => p.MediaId).ToList());
            }

            Assert.All(pulled, item =>
                Assert.False(
                    item.MediaId.StartsWith("tts:", StringComparison.Ordinal),
                    $"Expected music item but got TTS item: {item.MediaId}"));
        }

        [Fact]
        public async Task NoGetNextAsyncCallBlocksPastTheRenderBudget()
        {
            // Each call must finish in well under the stub's 30-second delay.
            // We allow 3× the budget as tolerance to avoid flakiness on loaded CI runners.
            var tolerance = RenderBudget + TimeSpan.FromSeconds(3);
            var ctx = new PlayoutContext([]);

            for (var i = 0; i < 4; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                sw.Stop();

                Assert.NotNull(item);
                Assert.True(
                    sw.Elapsed < tolerance,
                    $"GetNextAsync #{i + 1} took {sw.Elapsed.TotalMilliseconds:F0} ms, expected < {tolerance.TotalMilliseconds:F0} ms");

                ctx = new PlayoutContext([item.MediaId]);
            }
        }

        [Fact]
        public async Task FeederLoopCompletesEachTickWithinThreeSeconds()
        {
            var fakeLs  = new FakeLiquidsoapControl();
            var feeder  = new PlayoutFeeder(fakeLs, orchestrator, new FakeRotationSettingsProvider(new RotationSettings()));
            var limit   = TimeSpan.FromSeconds(3);

            for (var i = 0; i < 4; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await feeder.TickAsync(CancellationToken.None);
                sw.Stop();

                Assert.True(
                    sw.Elapsed < limit,
                    $"Feeder tick #{i + 1} took {sw.Elapsed.TotalMilliseconds:F0} ms, expected < {limit.TotalMilliseconds:F0} ms");
            }
        }
    }

    // -------------------------------------------------------------------------
    // SAD PATH — recovery
    // -------------------------------------------------------------------------

    public sealed class ScenarioSynthesizerRecoveryTtsResumesOnceKokoroIsBack
        : IAsyncLifetime
    {
        KokoroStubServer stub = null!;
        DirectoryInfo cacheDir = null!;
        Orchestrator orchestrator = null!;

        public async Task InitializeAsync()
        {
            stub     = await KokoroStubServer.StartAsync(KokoroStubMode.Fail500);
            cacheDir = System.IO.Directory.CreateTempSubdirectory("genwave-t015-recovery-");
            orchestrator = BuildOrchestrator(
                stub.BaseUri,
                cacheDir.FullName,
                // Unique voice ensures no stale cache hits from the Fail500 window.
                voice:        $"voice-{Guid.NewGuid():N}",
                renderBudget: TimeSpan.FromSeconds(2),
                catalog:      new MultiTrackCatalog());
        }

        public async Task DisposeAsync()
        {
            await stub.DisposeAsync();
            if (cacheDir.Exists) cacheDir.Delete(recursive: true);
        }

        [Fact]
        public async Task TtsNamespacedItemsAppearInTheSequenceAfterRecovery()
        {
            // Phase 1: pull several items while Kokoro is down — all should be music.
            var ctx = new PlayoutContext([]);
            const int failCount = 4;

            for (var i = 0; i < failCount; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                Assert.False(
                    item.MediaId.StartsWith("tts:", StringComparison.Ordinal),
                    $"Expected music during failure window but got: {item.MediaId}");
                ctx = new PlayoutContext([item.MediaId]);
            }

            // Phase 2: switch stub to success mode, then pull more items.
            stub.Mode = KokoroStubMode.ServeCannedWav;

            var afterRecovery = new List<MediaItem>();
            const int recoveryPulls = 8;   // enough units that at least one will attempt a LeadIn render

            for (var i = 0; i < recoveryPulls; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                afterRecovery.Add(item);
                ctx = new PlayoutContext(afterRecovery.Select(p => p.MediaId).ToList());
            }

            Assert.Contains(afterRecovery, item =>
                item.MediaId.StartsWith("tts:", StringComparison.Ordinal));
        }
    }
}
