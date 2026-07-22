// STORY-212 — The envelope is law, and silence is forbidden
//
// BDD specification — xUnit (SPEC F81.2, F81.5, F81.6). PLAN T62 wires the envelope-only
// INextItemProvider into the feeder — envelope re-check (discard+log+re-run), degradation ladder
// rotation→energy→genres with loud logs, never-silence. PLAN T62 wires the envelope-only
// INextItemProvider into the feeder — the ladder is proven as behavior of the production pick path
// (Orchestrator.GetNextAsync), not of a helper, with a fake IMediaCatalog adapter (Story007 idiom).

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureEnvelopeProviderAndLadder
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static MediaReference MakeRef(string id, string? genre) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: genre,
        Year: null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static (Orchestrator Orchestrator, CapturingLogger<Orchestrator> Logger) BuildOrchestrator(
        IMediaCatalog catalog,
        SegmentEnvelope envelope,
        IPersonaPickProvider? personaPickProvider = null,
        int artistSeparation = 0)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(SilentCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings { ArtistSeparation = artistSeparation });
        var logger = new CapturingLogger<Orchestrator>();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
            new FakeTtsSegmentSource(), new FakeActivePersonaAccessor(), logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            new FakeEnvelopeProvider(envelope),
            personaPickProvider);
        return (orchestrator, logger);
    }

    // -------------------------------------------------------------------------
    // HAPPY PATH
    // -------------------------------------------------------------------------

    public static class ScenarioPersonaLessOperation
    {
        // Arrange (T62): envelope-only provider, no persona layer registered, healthy pool.

        [Fact]
        public static async Task PicksAreEnvelopeConforming()
        {
            // F81.2: playout never depends on the persona layer existing
            var pool = new[] { MakeRef("rock1", "Rock"), MakeRef("jazz1", "Jazz") };
            var catalog = new FakeLadderMediaCatalog(pool);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var (orchestrator, _) = BuildOrchestrator(catalog, envelope);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("rock1", item.MediaId);
        }

        [Fact]
        public static async Task RotationScoreAloneOrdersThePool()
        {
            // No persona to bias the pick — whichever candidate the catalog's own rotation-tiered
            // query prefers (here: the one NOT in the recent list) is exactly what airs.
            var pool = new[] { MakeRef("r1", "Rock"), MakeRef("r2", "Rock") };
            var catalog = new FakeLadderMediaCatalog(pool);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var (orchestrator, _) = BuildOrchestrator(catalog, envelope);

            var item = await orchestrator.GetNextAsync(new PlayoutContext(["r1"]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("r2", item.MediaId);
        }
    }

    public static class ScenarioTrustButVerify
    {
        // Arrange (T62): a ranker stub that returns a track violating the envelope.

        [Fact]
        public static async Task AViolatingPickIsDiscarded()
        {
            // F81.5: feeder re-check rejects it
            var rock = MakeRef("rock1", "Rock");
            var catalog = new FakeLadderMediaCatalog([rock]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var violating = new RotationCandidate(MakeRef("jazz1", "Jazz"), false, false);
            var personaPickProvider = new FakePersonaPickProvider { NextResult = violating };
            var (orchestrator, _) = BuildOrchestrator(catalog, envelope, personaPickProvider);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("rock1", item.MediaId);
        }

        [Fact]
        public static async Task TheDiscardIsLogged()
        {
            var rock = MakeRef("rock1", "Rock");
            var catalog = new FakeLadderMediaCatalog([rock]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var violating = new RotationCandidate(MakeRef("jazz1", "Jazz"), false, false);
            var personaPickProvider = new FakePersonaPickProvider { NextResult = violating };
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, personaPickProvider);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Contains(logger.Warnings, w =>
                w.Contains("jazz1", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("envelope", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task ThePickReRunsEnvelopeOnly()
        {
            // the replacement comes from the envelope-only path, same cycle (F81.5) — the persona
            // pick provider is consulted exactly once (never retried); the envelope-only ladder
            // supplies the actual replacement.
            var rock = MakeRef("rock1", "Rock");
            var catalog = new FakeLadderMediaCatalog([rock]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var violating = new RotationCandidate(MakeRef("jazz1", "Jazz"), false, false);
            var personaPickProvider = new FakePersonaPickProvider { NextResult = violating };
            var (orchestrator, _) = BuildOrchestrator(catalog, envelope, personaPickProvider);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("rock1", item.MediaId);
            Assert.Single(personaPickProvider.Calls);
            Assert.Single(catalog.EnvelopeCallEnvelopes);
        }
    }

    // -------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------

    public static class SadPathDegradationLadder
    {
        // Arrange (T62): envelopes engineered to produce an empty pool at each rung.

        [Fact]
        public static async Task RotationRelaxesFirst()
        {
            // F81.6 order: rotation (hygiene) before any law bends. The one seeded track already
            // conforms to the envelope — NullUntilCallNumber forces rung 1 empty regardless, so
            // rung 2 (rotation relaxed) is the one that actually succeeds.
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Rock")]) { NullUntilCallNumber = 2 };
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, artistSeparation: 2);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Equal(2, catalog.EnvelopeCallEnvelopes.Count);
            Assert.Contains(logger.Warnings, w => w.Contains("rotation", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("energy band", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("genre allow-list to admit", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task EnergyRelaxesSecond()
        {
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Rock")]) { NullUntilCallNumber = 3 };
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], new EnergyRange(0.3, 0.7));
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, artistSeparation: 2);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Equal(3, catalog.EnvelopeCallEnvelopes.Count);
            Assert.Equal(EnergyRange.Unconstrained, catalog.EnvelopeCallEnvelopes[2].EnergyRange);
            Assert.Contains(logger.Warnings, w => w.Contains("rotation", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Warnings, w => w.Contains("energy band", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("genre allow-list to admit", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task GenresRelaxLast()
        {
            // The seeded track is untagged for this envelope (Jazz, not Rock) — rungs 1-3 all keep
            // the genre allow-list intact and stay empty; only rung 4 (Genres=[]) admits it.
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Jazz")]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], new EnergyRange(0.3, 0.7));
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, artistSeparation: 2);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Equal(4, catalog.EnvelopeCallEnvelopes.Count);
            Assert.Empty(catalog.EnvelopeCallEnvelopes[3].Genres);
            Assert.Contains(logger.Warnings, w => w.Contains("genre allow-list to admit", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task EachRelaxationLogsLoudly()
        {
            // one warn per rung naming the relaxed constraint (F81.6)
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Jazz")]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], new EnergyRange(0.3, 0.7));
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, artistSeparation: 2);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal(3, logger.Warnings.Count(w => w.Contains("relax", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(logger.Warnings, w => w.Contains("rotation", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Warnings, w => w.Contains("energy band", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Warnings, w => w.Contains("genre allow-list to admit", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task AnEmptyGenreNeverSilencesTheStation()
        {
            // envelope naming a zero-track genre still yields a pick (F81.6 never-silence). Every
            // envelope-aware rung (1-4) is forced empty; only the terminal, pre-envelope query finds
            // the track.
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Rock")]) { NullUntilCallNumber = 5 };
            var envelope = new SegmentEnvelope(
                TimeOnly.MinValue, TimeOnly.MaxValue, ["no-such-genre"], new EnergyRange(0.3, 0.7));
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, artistSeparation: 2);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Equal(1, catalog.RotationCallCount);
            Assert.Contains(logger.Warnings, w =>
                w.Contains("never-silence", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task APersonaLayerThrowDegradesToEnvelopeOnly()
        {
            // ranker throws/times out ⇒ envelope-only pick, mode not error (F81.6)
            var catalog = new FakeLadderMediaCatalog([MakeRef("t1", "Rock")]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);
            var personaPickProvider = new FakePersonaPickProvider
            {
                ThrowOnPick = new InvalidOperationException("ranker boom"),
            };
            var (orchestrator, logger) = BuildOrchestrator(catalog, envelope, personaPickProvider);

            // A throwing personaPickProvider must never escape GetNextAsync as a faulted Task (mode
            // not error, SPEC F81.6) — an unhandled exception here would fail this test on its own.
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Contains(logger.Warnings, w =>
                w.Contains("persona", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("degrad", StringComparison.OrdinalIgnoreCase));
        }
    }
}
