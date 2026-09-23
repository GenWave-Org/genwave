// STORY-456 — The speaker travels with the plan — the render branch (gh-#772 · SPEC F189.3, F189.6 · PLAN T525, T526)
//
// BDD specification — xUnit. AC2–AC5 drive TtsSegmentSource with a recording synthesizer and ambient caches that throw on refresh; AC12 drives
// the Tts SpeakerSnapshotSource with an unknown id.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSnapshotDrivesTheRender
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Minimal, valid <see cref="PersonaCard"/> carrying only the pace under test (mirrors
    /// Story255's own <c>CardWithPace</c>) — used only by the ambient scenario below, which still
    /// exercises today's persona-card-derived pace path unchanged.
    /// </summary>
    static PersonaCard CardWithPace(double pace) =>
        new(
            SchemaVersion: 1,
            Name: "Test Persona",
            Tagline: "Test tagline",
            Soul: "Test soul",
            Quirks: [],
            Voice: new VoiceSpec(Engine: "", VoiceId: "af_heart", Pace: pace, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: []);

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static SpeakerSnapshot Snapshot(string voice, double pace, string contentHash) =>
        new(
            PersonaId: 7, PersonaName: "Probe", Voice: voice, Pace: pace,
            Rules: [new GenWave.Core.Domain.PronunciationRule("Nguyen", "Nguyen", "ˈwɪn")], Corrections: [],
            ContentHash: contentHash);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioARequestWithASnapshot : IAsyncLifetime
    {
        // Given: Speaker pace 1.2 and one pronunciation rule
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        public async Task InitializeAsync()
        {
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Nguyen is coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(), opts, NullLogger<TtsSegmentSource>.Instance);
            var snapshot = Snapshot(voice: "af_heart", pace: 1.2, contentHash: "hash-a");
            var request = StationIdRequest() with { Speaker = snapshot };

            await source.RenderAsync(request, CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>AC2 — the synthesizer received 1.2</summary>
        [Fact]
        public void SendsTheSnapshotPace() => Assert.Equal(1.2, synth.LastContext?.Pace);

        /// <summary>AC2 — the rule rode on the context</summary>
        [Fact]
        public void AppliesTheSnapshotRule() => Assert.Contains(synth.LastContext?.Rules ?? [], r => r.Word == "Nguyen");
    }

    public sealed class ScenarioARequestWithASnapshotAndThrowingCaches : IAsyncLifetime
    {
        // Given: ambient caches throw on refresh
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        MediaItem? result;

        public async Task InitializeAsync()
        {
            var throwingAccessor = new FakeActivePersonaAccessor { ThrowOnResolve = new InvalidOperationException("card store down") };
            var personaCorrections = new ActivePersonaCorrectionsCache(throwingAccessor, TimeProvider.System);
            var personaPronunciations = new ActivePersonaPronunciationRulesCache(throwingAccessor, TimeProvider.System);
            var personaPace = new ActivePersonaPaceCache(throwingAccessor, TimeProvider.System, NullLogger<ActivePersonaPaceCache>.Instance);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), personaCorrections, NoCorrections.PronunciationProvider(),
                personaPronunciations, personaPace, opts, NullLogger<TtsSegmentSource>.Instance);
            var snapshot = Snapshot(voice: "af_heart", pace: 1.0, contentHash: "hash-a");
            var request = StationIdRequest() with { Speaker = snapshot };

            result = await source.RenderAsync(request, CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>AC3 — the snapshot bypasses the caches</summary>
        [Fact]
        public void RendersSuccessfully() => Assert.NotNull(result);
    }

    public sealed class ScenarioThePlannedCorrectionAppliesWithAFaultingAmbientCache : IAsyncLifetime
    {
        // Given: a TtsRenderContext whose Corrections carries a planned correction, driving the REAL
        // NormalizingTtsSynthesizer — the one class that still calls
        // ActivePersonaCorrectionsCache.RefreshIfStaleAsync (SPEC F189.3) — over a real
        // ActivePersonaCorrectionsCache whose own accessor throws on resolve. Fixture shape copied
        // from Gh491_RulesOverCorrections (the closest precedent for exercising the normalizer
        // itself, rather than TtsSegmentSource, with a real corrections pipeline).
        readonly FakeTtsSynthesizer inner = new();
        string? rendered;

        public async Task InitializeAsync()
        {
            var throwingAccessor = new FakeActivePersonaAccessor { ThrowOnResolve = new InvalidOperationException("card store down") };
            var personaCorrections = new ActivePersonaCorrectionsCache(throwingAccessor, TimeProvider.System);
            var normalizer = new NormalizingTtsSynthesizer(
                inner, NoCorrections.Provider(), personaCorrections, new CorrectionsFiredStats(),
                NullLogger<NormalizingTtsSynthesizer>.Instance);
            var context = new TtsRenderContext("Now playing: MacLeod.", "af_heart", Kind: null)
            {
                Corrections = [new SpeechCorrection("MacLeod", "Maa-cloud")],
            };

            rendered = await normalizer.SynthesizeAsync(context, CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>F189.3 — the render succeeds despite the faulting ambient corrections cache.</summary>
        [Fact]
        public void RendersSuccessfully() => Assert.NotNull(rendered);

        /// <summary>
        /// F189.3 — the planned correction fired on the text that reached the inner synthesizer.
        /// The loose colon now speaks as a comma (SPEC F198.1, gh-#703) rather than closing up to
        /// nothing — unrelated to this fact's own faulting-cache resilience.
        /// </summary>
        [Fact]
        public void AppliesThePlannedCorrection() => Assert.Equal("now playing, maa-cloud.", inner.LastText);
    }

    public sealed class ScenarioTwoSnapshotsSameText : IAsyncLifetime
    {
        // Given: identical text, ContentHash differs
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        public async Task InitializeAsync()
        {
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(), opts, NullLogger<TtsSegmentSource>.Instance);

            await source.RenderAsync(
                StationIdRequest() with { Speaker = Snapshot(voice: "af_heart", pace: 1.0, contentHash: "hash-a") }, CancellationToken.None);
            await source.RenderAsync(
                StationIdRequest() with { Speaker = Snapshot(voice: "af_heart", pace: 1.0, contentHash: "hash-b") }, CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>AC4 — the cache key carries the snapshot</summary>
        [Fact]
        public void SynthesizesTwice() => Assert.Equal(2, synth.CallCount);
    }

    public sealed class ScenarioARequestWithoutASnapshot : IAsyncLifetime
    {
        // Given: Speaker null, ambient caches at pace 0.9
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        public async Task InitializeAsync()
        {
            var accessor = new FakeActivePersonaAccessor { Card = CardWithPace(0.9) };
            var personaPace = new ActivePersonaPaceCache(accessor, TimeProvider.System, NullLogger<ActivePersonaPaceCache>.Instance);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), personaPace, opts, NullLogger<TtsSegmentSource>.Instance);

            await source.RenderAsync(StationIdRequest(), CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>AC5 — today's path unchanged</summary>
        [Fact]
        public void SendsTheAmbientPace() => Assert.Equal(0.9, synth.LastContext?.Pace);
    }

    public sealed class ScenarioASnapshotVoiceDisagreesWithTheRequestVoice : IAsyncLifetime
    {
        // Given: a snapshot whose Voice does not match the request's own Voice (a caller bug, not
        // a render failure — see ResolveFromSnapshot's own remarks).
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly CapturingLogger<TtsSegmentSource> logger = new();

        public async Task InitializeAsync()
        {
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(), opts, logger);
            var snapshot = Snapshot(voice: "bf_emma", pace: 1.0, contentHash: "hash-a");

            await source.RenderAsync(StationIdRequest() with { Speaker = snapshot }, CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>A mismatch logs exactly one WARN, naming the snapshot's voice.</summary>
        [Fact]
        public void LogsOneWarn() => Assert.Single(logger.Warnings, w => w.Contains("bf_emma"));
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownPersonaId : IAsyncLifetime
    {
        // Given: the snapshot source resolves an id that does not exist
        const long UnknownPersonaId = 4242;

        readonly CapturingLogger<SpeakerSnapshotSource> logger = new();
        SpeakerSnapshot snapshot = new(null, null, "", TtsPace.EngineDefault, [], [], "");

        public async Task InitializeAsync()
        {
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("genwave-1", "Test", "af_heart"));
            var cards = new FakePersonaCardByIdSource();
            var pronunciations = new PronunciationRuleProvider(
                new TestOptionsMonitor<TtsPronunciationsOptions>(new TtsPronunciationsOptions()),
                NullLogger<PronunciationRuleProvider>.Instance);
            var corrections = new SpeechCorrectionProvider(
                new TestOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
                NullLogger<SpeechCorrectionProvider>.Instance);

            var source = new SpeakerSnapshotSource(cards, identityProvider, pronunciations, corrections, logger);
            snapshot = await source.ForPersonaAsync(UnknownPersonaId, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC12 — the unknown id degrades to the station's own snapshot.</summary>
        [Fact]
        public void FallsBackToTheStationSnapshot()
        {
            Assert.Null(snapshot.PersonaId);
            Assert.Equal("af_heart", snapshot.Voice);
        }

        /// <summary>AC12 — exactly one WARN, naming the id.</summary>
        [Fact]
        public void LogsOneWarnNamingTheId() => Assert.Single(logger.Warnings, w => w.Contains("4242"));
    }
}
