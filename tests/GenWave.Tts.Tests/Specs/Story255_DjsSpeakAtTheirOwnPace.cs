// STORY-255 — DJs speak at their own pace (SPEC F98)
//
// `VoiceSpec.Pace` — "speaking-rate multiplier; 1.0 is engine default" — is in the persona
// card, in the export/import contract, and in the community catalog's published JSON schema.
// Nothing reads it. kokoro-fastapi's /v1/audio/speech takes `speed`; the adapter sends
// { input, voice, response_format } and never has.
//
// The consequence is measurable: 11 of the 12 first-party catalog personas carry a deliberate
// non-default pace (Rusty Strings 0.85 slow and weathered, Maxxie Volt 1.15 fast and
// energetic) and all twelve currently speak at an identical rate.
//
// ⚠️ F98.3 — this knowingly departs from the house "sounds identical on upgrade" discipline.
// The new sound is the one the persona's author specified; the uniformity IS the defect.
//
// The cache-key half is a correctness requirement, not a nicety: audio rendered at 0.85 is not
// audio rendered at 1.0, and pace is invisible in the text, so a key that ignores it serves a
// persona's old rate forever after an edit.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureDjsSpeakAtTheirOwnPace
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    static (HttpClient Http, List<string> Bodies) WireCapture()
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        });
        return (new HttpClient(handler), bodies);
    }

    static double SpeedOf(string requestBody) =>
        JsonDocument.Parse(requestBody).RootElement.GetProperty("speed").GetDouble();

    /// <summary>
    /// Minimal, valid <see cref="PersonaCard"/> carrying only the pace under test — every other
    /// field is filler content the pace-cache-key specs below don't assert on (mirrors
    /// Story005's CardWithCorrections/CardWithPronunciation).
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

    public static class ScenarioPaceReachesTheEngine
    {
        [Fact]
        public static async Task The_kokoro_request_body_carries_speed()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var synth = new KokoroTtsSynthesizer(
                    http, new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }));

                await synth.SynthesizeAsync(
                    new TtsRenderContext("Coming up next", "af_heart", SegmentKind.LeadIn) { Pace = 0.85 },
                    CancellationToken.None);

                Assert.Equal(0.85, SpeedOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task The_default_pace_is_sent_as_the_engine_default()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var synth = new KokoroTtsSynthesizer(
                    http, new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }));

                // The plain two-arg overload — every caller with no TtsRenderContext to draw a
                // resolved pace from (safe/authored segments, the admin preview endpoint).
                await synth.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

                Assert.Equal(1.0, SpeedOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static void The_render_context_carries_pace_from_the_persona()
        {
            // TtsRenderContext widened per F70.3's precedent — a default interface member, so
            // every existing engine client and test fake compiles and behaves unchanged.
            var context = new TtsRenderContext("Coming up next", "af_heart", SegmentKind.LeadIn)
                with { Pace = 0.85 };

            Assert.Equal(0.85, context.Pace);
        }
    }

    public static class ScenarioBothCacheKeysHonourPace
    {
        [Fact]
        public static async Task The_segment_cache_separates_two_paces()
        {
            // Same copy, same voice, pace 0.85 vs 1.0 → two distinct segment-cache entries.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            try
            {
                var accessor = new FakeActivePersonaAccessor { Card = CardWithPace(0.85) };
                var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
                var personaPace = new ActivePersonaPaceCache(accessor, clock, NullLogger<ActivePersonaPaceCache>.Instance);
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var source = new TtsSegmentSource(
                    new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                    NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                    NoCorrections.PersonaPronunciationCache(), personaPace, opts, NullLogger<TtsSegmentSource>.Instance);
                var request = StationIdRequest();

                var slow = await source.RenderAsync(request, CancellationToken.None);

                accessor.Card = CardWithPace(1.0);
                clock.Advance(ActivePersonaPaceCache.StalenessBound);
                var normal = await source.RenderAsync(request, CancellationToken.None);

                Assert.NotEqual(slow!.MediaId, normal!.MediaId);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }

        // "The engine file cache separates two paces" is RETIRED, not activated: F98.2 as amended
        // at T138's review found the engine adapters' transient write was never a cache at all — a
        // write-only, content-addressed name nothing ever read back by (see TransientRenderPath's
        // remarks for the full root cause) — and T138 replaced it with a fresh Guid per call,
        // collision-safe but deliberately un-keyed by anything, pace included. There is exactly ONE
        // render cache in this system, TtsSegmentSource's own (pinned by
        // The_segment_cache_separates_two_paces above); a second "engine file cache" fact would
        // pin a shape that does not exist.

        [Fact]
        public static async Task An_unchanged_pace_still_hits_the_cache()
        {
            // Adding a key term must not defeat caching for the overwhelmingly common case.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            try
            {
                var accessor = new FakeActivePersonaAccessor { Card = CardWithPace(0.85) };
                var personaPace = new ActivePersonaPaceCache(accessor, TimeProvider.System, NullLogger<ActivePersonaPaceCache>.Instance);
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var source = new TtsSegmentSource(
                    new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                    NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                    NoCorrections.PersonaPronunciationCache(), personaPace, opts, NullLogger<TtsSegmentSource>.Instance);
                var request = StationIdRequest();

                var first = await source.RenderAsync(request, CancellationToken.None);
                Assert.Equal(1, synth.CallCount);

                var second = await source.RenderAsync(request, CancellationToken.None);
                Assert.Equal(1, synth.CallCount); // still 1 — a genuine cache hit
                Assert.Equal(first!.MediaId, second!.MediaId);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the audible claim. A hash test proves keys differ; only a real render
    // proves a persona actually sounds slower.
    // -------------------------------------------------------------------------------------
    public static class ScenarioARealRenderChangesRate
    {
        [Fact(Skip = "Manual by design — verified live at T141's wire smoke (speed on the wire at 0.85/1.15, ~25% duration delta; see the T141 evidence commit). A real engine render cannot be a CI fact.")]
        public static void A_slow_persona_renders_longer_audio_than_a_fast_one()
        {
            // Same copy through the production graph at 0.85 and 1.15; compare measured
            // durations, not request bodies.
            Assert.Fail("pending T141");
        }

        [Fact(Skip = "Manual by design — verified live at T141's wire smoke (speed on the wire at 0.85/1.15, ~25% duration delta; see the T141 evidence commit). A real engine render cannot be a CI fact.")]
        public static void Editing_a_personas_pace_produces_fresh_audio()
        {
            // The regression this guards: serving the cached 1.0 clip after an edit to 1.15.
            Assert.Fail("pending T141");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------

    // T140 precondition (c): NaN/Infinity throw inside System.Text.Json's JsonSerializer, and
    // zero/negative describe no honest playback rate at all — none of the four is a render
    // failure. TtsPace.Clamp is the ONE classify-and-clamp seam every one of these degenerate
    // inputs passes through before ever reaching a context or a cache key (see that class's own
    // remarks for the full "why validate at all" chain of consequences).
    public static class ScenarioDegenerateValuesClampToTheEngineDefault
    {
        [Fact]
        public static void NaN_resolves_to_the_engine_default()
        {
            var resolved = TtsPace.Clamp(double.NaN);

            Assert.Equal(1.0, resolved);
        }

        [Fact]
        public static void Infinity_resolves_to_the_engine_default()
        {
            var resolved = TtsPace.Clamp(double.PositiveInfinity);

            Assert.Equal(1.0, resolved);
        }

        [Fact]
        public static void Zero_resolves_to_the_engine_default()
        {
            var resolved = TtsPace.Clamp(0);

            Assert.Equal(1.0, resolved);
        }

        [Fact]
        public static void A_negative_pace_resolves_to_the_engine_default()
        {
            var resolved = TtsPace.Clamp(-0.5);

            Assert.Equal(1.0, resolved);
        }
    }

    // A finite, positive value outside kokoro-fastapi's own [0.5, 2.0] window still describes an
    // honest rate — "as slow/fast as the engine allows" — so it clamps to the nearest bound
    // rather than resetting to the engine default the way a degenerate value does above.
    public static class ScenarioHonestOutOfRangeValuesClampToTheEngineBound
    {
        [Fact]
        public static void A_pace_below_the_engines_minimum_clamps_to_the_minimum()
        {
            var resolved = TtsPace.Clamp(0.1);

            Assert.Equal(0.5, resolved);
        }

        [Fact]
        public static void A_pace_above_the_engines_maximum_clamps_to_the_maximum()
        {
            var resolved = TtsPace.Clamp(5.0);

            Assert.Equal(2.0, resolved);
        }
    }

    // Review finding: the WARN is a WarnOnce LATCH, not a per-poll log — a card that never gets
    // corrected must not re-log the identical WARN on every single StalenessBound refresh forever.
    // ActivePersonaPaceCache.RefreshIfStaleAsync is the ONE call site that owns both the logging
    // and the latch (TtsPace itself stays pure/stateless — see its own remarks); this scenario
    // pins the latch at that seam, not at TtsPace.Clamp, which never logs at all.
    public static class ScenarioAStandingBadCardWarnsOnce
    {
        [Fact]
        public static async Task Two_refreshes_of_the_same_bad_card_log_exactly_one_warning()
        {
            var accessor = new FakeActivePersonaAccessor { Card = CardWithPace(double.NaN) };
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var logger = new CapturingLogger<ActivePersonaPaceCache>();
            var cache = new ActivePersonaPaceCache(accessor, clock, logger);

            await cache.RefreshIfStaleAsync(CancellationToken.None);
            clock.Advance(ActivePersonaPaceCache.StalenessBound);
            await cache.RefreshIfStaleAsync(CancellationToken.None);

            Assert.Equal(1, logger.Warnings.Count(w => w.Contains("Test Persona", StringComparison.Ordinal)));
        }
    }

    public static class ScenarioEnginesWithoutRateControl
    {
        [Fact]
        public static async Task A_rate_less_engine_renders_successfully_anyway()
        {
            // F98.1 — Piper has no rate-control mechanism at all: it never reads
            // TtsRenderContext.Pace (unlike Rules/PiperSpeechMarkup, there is no strip step either
            // — the field is simply never looked at), so an extreme pace never becomes a render
            // failure on this engine, degenerate or not.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var renderer = new PiperTtsSynthesizer(
                    http, new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }));
                var profile = new TtsFallbackProfile
                    { Engine = DependencyNames.Piper, Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" };
                var context = new TtsRenderContext("Coming up next", "af_heart", Kind: null) { Pace = double.NaN };

                var path = await renderer.RenderAsync(profile, context, CancellationToken.None);

                Assert.NotNull(path);
                Assert.Equal("Coming up next", Assert.Single(bodies));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }
}
