// gh-#117 — The DJ's clock follows the station, not the container: SegmentRequest.LocalNow half.
//
// BDD specification — xUnit. The setting/validator/provider halves live in
// Host.Tests/Specs/Gh117_StationTimezoneSetting.cs and the prompt clock-line half in
// Tts.Tests/Specs/Story193_PersonaPromptAssemblyAndClock.cs (the Story117/121 split: facts live
// where their subject compiles). What THIS file owns: the Orchestrator stamps every
// SegmentRequest.LocalNow — the value the templated time/date patter ("It's {LocalNow:h:mm tt}")
// and the LLM's "Local time" line both render from — through the IStationClockProvider seam when
// one is wired, and stays byte-identical to the pre-gh-#117 UTC stamp when none is (every rig
// built before the seam existed).
//
// Rig mirrors Story131_PersonaAttributionRequestShape's LeadIn-only cadence: exactly one segment
// render per unit, so LastRequest unambiguously reflects the request under assertion.

namespace GenWave.Orchestration.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureStationLocalSegmentClock
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // 20:00 UTC on 2026-01-10 — 13:00 in Edmonton (MST, UTC-7), the zone the station clock fake
    // hands back below.
    static readonly DateTimeOffset FixedUtc = new(2026, 1, 10, 20, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset EdmontonWallClock = new(2026, 1, 10, 13, 0, 0, TimeSpan.FromHours(-7));

    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    // LeadIn-only cadence: exactly one segment render per unit (the Story131 idiom).
    static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts) BuildOrchestrator(
        IStationClockProvider? stationClock)
    {
        var cadence = new CadenceConfig
        {
            LeadInBeforeEachTrack = true,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        };
        var tts = new FakeTtsSegmentSource();
        var orchestrator = new Orchestrator(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "af_heart")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new FakeMediaCatalog(MakeRef("track1")),
            tts,
            new FakeActivePersonaAccessor(),
            NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            new SpeechDeferralQueue(new FakeTimeProvider(FixedUtc)),
            new FakeTimeProvider(FixedUtc),
            new FakeBoundaryBiasProvider(TimeSpan.Zero),
            stationClock: stationClock);
        return (orchestrator, tts);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a wired station clock stamps station-local wall time
    // ---------------------------------------------------------------------

    public sealed class ScenarioAWiredStationClockStampsStationWallTime
    {
        [Fact]
        public async Task LocalNowIsTheStationClocksZonedValueOffsetIncluded()
        {
            // gh-#117 — the request the patter/LLM path renders time from carries the station's
            // own wall clock, exactly as the seam handed it over (offset preserved, never
            // silently re-normalized to UTC).
            var (orchestrator, tts) = BuildOrchestrator(new FakeStationClockProvider(EdmontonWallClock));

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var localNow = tts.LastRequest?.LocalNow;
            Assert.NotNull(localNow);
            Assert.Equal(TimeSpan.FromHours(-7), localNow.Value.Offset);
            Assert.Equal(new DateTime(2026, 1, 10, 13, 0, 0), localNow.Value.DateTime);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioNoStationClockKeepsThePriorUtcStamp
    {
        [Fact]
        public async Task LocalNowFallsBackToTheTimeProvidersUtcNow()
        {
            // Empty Station:Timezone resolves to no different container behavior, and a rig with
            // no seam at all (every pre-gh-#117 composition) stamps the injected TimeProvider's
            // UTC now — the old raw DateTimeOffset.UtcNow, made deterministic.
            var (orchestrator, tts) = BuildOrchestrator(stationClock: null);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var localNow = tts.LastRequest?.LocalNow;
            Assert.NotNull(localNow);
            Assert.Equal(TimeSpan.Zero, localNow.Value.Offset);
            Assert.Equal(FixedUtc.DateTime, localNow.Value.DateTime);
        }
    }
}
