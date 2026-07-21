// STORY-191 — Per-kind TTS engine override
//
// BDD specification — xUnit (SPEC F70.3). Implemented PLAN T35 (/build-loop).
//
// FallbackTtsSynthesizer is the router under test — same shape as Story190's own fixture helpers
// (BuildRouter, ConfiguredFallbackOptions), extended with an optional TtsEngineByKindProvider so a
// scenario can supply (or omit) a Tts:EngineByKind map.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePerKindEngineOverride
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    static TestOptionsMonitor<TtsFallbackOptions> ConfiguredFallbackOptions() =>
        new(new TtsFallbackOptions { Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" });

    static TtsEngineByKindProvider BuildOverrides(string json) =>
        new(
            new TestOptionsMonitor<TtsEngineByKindOptions>(new TtsEngineByKindOptions { EngineByKind = json }),
            NullLogger<TtsEngineByKindProvider>.Instance);

    static FallbackTtsSynthesizer BuildRouter(
        FakeTtsSynthesizer primary,
        FakeTtsSynthesizer fallback,
        FakeDependencyHealth health,
        TtsEngineByKindProvider? engineOverrides = null,
        TestOptionsMonitor<TtsFallbackOptions>? fallbackOptions = null,
        CapturingLogger<FallbackTtsSynthesizer>? logger = null) =>
        new(primary, fallback, health, fallbackOptions ?? ConfiguredFallbackOptions(),
            logger ?? new CapturingLogger<FallbackTtsSynthesizer>(), engineOverrides);

    static TtsRenderContext StationIdContext() =>
        new("Coming up next", "af_heart", SegmentKind.StationId);

    static DependencyHealthVerdict UnhealthyKokoroVerdict() =>
        new(DependencyNames.Kokoro, Healthy: false, DateTimeOffset.UtcNow, "connect failure", ConsecutiveFailureCount: 3);

    // ------------------------------------------------------------------
    // HAPPY PATH
    // ------------------------------------------------------------------

    public static class ScenarioMappedKind
    {
        [Fact]
        public static async Task Mapped_kind_renders_on_the_mapped_engine()
        {
            // Given a map entry StationId → Piper, and a HEALTHY Kokoro verdict — without the
            // override, StationId would render on Kokoro
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var overrides = BuildOverrides("""{"StationId":"piper"}""");
            var router = BuildRouter(primary, fallback, health, overrides);

            // When an ident (StationId) renders
            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then the mapped engine (Piper) renders it (F70.3) — the health-based routing that
            // would otherwise pick Kokoro is pre-empted in the forward direction.
            Assert.NotNull(path);
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(1, fallback.CallCount);
        }
    }

    public static class ScenarioFallthrough
    {
        [Fact]
        public static async Task Unmapped_kind_renders_on_the_default_engine()
        {
            // Given a map with an entry for a DIFFERENT kind — StationId itself is not mapped
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var overrides = BuildOverrides("""{"LeadIn":"piper"}""");
            var router = BuildRouter(primary, fallback, health, overrides);

            // When it renders
            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then the default engine (Kokoro, healthy) renders it (F70.3) — an unmapped kind falls
            // through to the existing F70.1 routing, unchanged by the presence of a map for OTHER
            // kinds.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);
        }
    }

    public static class ScenarioEmptyDefault
    {
        [Fact]
        public static async Task No_map_configured_is_identical_to_pre_feature_routing_when_healthy()
        {
            // Given no override map configured at all (null provider — the DI default when
            // Tts:EngineByKind is unset) and a healthy Kokoro verdict
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var router = BuildRouter(primary, fallback, health, engineOverrides: null);

            // When any kind renders
            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then behavior is identical to pre-feature (T34) routing: the healthy default engine
            // renders it, exactly as Story190's ScenarioFallthroughOnFailure precedent.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);
        }

        [Fact]
        public static async Task No_map_configured_is_identical_to_pre_feature_routing_when_unhealthy()
        {
            // The other half of "identical to pre-feature routing" (F70.1's own unhealthy-verdict
            // path, STORY-190 AC1) — an empty override map must not interfere with it either.
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyKokoroVerdict());
            var router = BuildRouter(primary, fallback, health, engineOverrides: null);

            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            Assert.NotNull(path);
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(1, fallback.CallCount);
        }
    }

    // ------------------------------------------------------------------
    // SAD PATH
    // ------------------------------------------------------------------

    public static class ScenarioMappedEngineFails
    {
        [Fact]
        public static async Task Mapped_engine_failure_falls_through_to_the_other_engine()
        {
            // Given StationId mapped to Piper, but Piper throws on render
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("piper down") };
            var health = new FakeDependencyHealth();
            var overrides = BuildOverrides("""{"StationId":"piper"}""");
            var router = BuildRouter(primary, fallback, health, overrides);

            // When the ident renders
            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then resilience stays symmetric (F70.1/F70.3): the render still falls through to
            // Kokoro and the segment still airs — a mapped engine is not exempt from fallthrough.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);   // never completed — it threw
        }
    }

    public static class ScenarioNullEngineValue
    {
        [Fact]
        public static async Task Null_engine_value_degrades_that_entry_only()
        {
            // Given a map where StationId's engine value is JSON null (e.g. a stale DB row, or a
            // hand-edited override that bypassed SettingValidator) and LeadIn is validly mapped
            // to Piper — STJ deserializes a JSON null property value into a CLR null despite the
            // Dictionary<string, string?> value type; a bare engine.Trim() on that null would NRE
            // at both singleton construction and the OnChange live-reload callback (review
            // finding, HIGH).
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var overrides = BuildOverrides("""{"StationId":null,"LeadIn":"piper"}""");
            var router = BuildRouter(primary, fallback, health, overrides);

            // When the null-mapped kind (StationId) renders — construction and this render must
            // not throw
            var stationIdPath = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then StationId falls through to the default (healthy) engine, exactly as an
            // unmapped kind would — the null entry degrades on its own, it does not crash the
            // whole map.
            Assert.NotNull(stationIdPath);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);

            // And the OTHER entry (LeadIn → piper) still routes correctly — the degrade is scoped
            // to the one bad entry, not the whole map.
            var leadInContext = new TtsRenderContext("Up next", "af_heart", SegmentKind.LeadIn);
            var leadInPath = await router.SynthesizeAsync(leadInContext, CancellationToken.None);

            Assert.NotNull(leadInPath);
            Assert.Equal(1, primary.CallCount);    // unchanged — LeadIn routed straight to fallback
            Assert.Equal(1, fallback.CallCount);
        }
    }

    public static class ScenarioNumericKindKey
    {
        [Fact]
        public static async Task Numeric_kind_key_is_ignored()
        {
            // Given a map keyed by the numeric string "0" (SegmentKind.StationId's own underlying
            // int value) rather than its name — Enum.TryParse<SegmentKind> alone accepts this,
            // contradicting SettingValidator's own rejection-message contract (review finding, LOW).
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var overrides = BuildOverrides("""{"0":"piper"}""");
            var router = BuildRouter(primary, fallback, health, overrides);

            // When StationId renders
            var path = await router.SynthesizeAsync(StationIdContext(), CancellationToken.None);

            // Then the numeric key is ignored — StationId is NOT treated as mapped to Piper; it
            // falls through to the default (healthy) engine exactly as if no map were configured.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);
        }
    }
}
