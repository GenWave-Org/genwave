// STORY-121 — The active persona flavors copy and voice (voice half)
//
// BDD specification — xUnit. The Orchestrator resolves SegmentRequest.Voice through the
// ActivePersonaAccessor per render — persona voice when non-empty, else Station:Voice.
// Prompt sections are the Tts half (Story121_PersonaPromptSections). Landed T6.
// See docs/PLAN.md Epic T.
//
// Revised by gh-#96: StationId is carved out of persona voice resolution — station IDs are
// station imaging and always render with the station's own voice and credit (real-radio
// convention: the ID is the brand speaking, not the DJ). ScenarioStationIdsAreStationImaging
// below pins that carve-out; the original scenarios keep pinning the persona rule for the
// DJ-spoken kinds.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePersonaVoiceResolution
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Persona BuildPersona(string voice) =>
        new(1, "DJ Nova", "", "", voice, DateTime.UtcNow, DateTime.UtcNow);

    // LeadIn-only cadence: exactly one segment render per unit, so LastRequest unambiguously
    // reflects the voice this fact is asserting on.
    static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts) BuildOrchestrator(
        FakeActivePersonaAccessor accessor, string stationVoice = "af_heart")
    {
        var cadence = new CadenceConfig
        {
            LeadInBeforeEachTrack = true,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        };
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", stationVoice));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeRef("track1"));
        var tts = new FakeTtsSegmentSource();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts, accessor,
            NullLogger<Orchestrator>.Instance, new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
        return (orchestrator, tts);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — voice follows the active persona
    // ---------------------------------------------------------------------

    public sealed class ScenarioResolvingTheSegmentVoice
    {
        [Fact]
        public async Task PersonaVoiceWinsWhenNonEmpty()
        {
            // Active persona voice "am_onyx" → SegmentRequest.Voice == "am_onyx" (F35.3, AC2).
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("am_onyx") };
            var (orchestrator, tts) = BuildOrchestrator(accessor);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(tts.LastRequest);
            Assert.Equal("am_onyx", tts.LastRequest.Voice);
        }

        [Fact]
        public async Task EmptyPersonaVoiceFallsBackToStationVoice()
        {
            // voice '' → Station:Voice (F35.3, AC3).
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("") };
            var (orchestrator, tts) = BuildOrchestrator(accessor, stationVoice: "af_heart");

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(tts.LastRequest);
            Assert.Equal("af_heart", tts.LastRequest.Voice);
        }

        [Fact]
        public async Task NoActivePersonaFallsBackToStationVoice()
        {
            // Accessor yields null → Station:Voice (F35.2, AC4).
            var accessor = new FakeActivePersonaAccessor(); // Persona stays null
            var (orchestrator, tts) = BuildOrchestrator(accessor, stationVoice: "af_heart");

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(tts.LastRequest);
            Assert.Equal("af_heart", tts.LastRequest.Voice);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — station IDs are station imaging, never the DJ (gh-#96)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationIdsAreStationImaging
    {
        // StationIdEveryNUnits=1 + LeadInBeforeEachTrack=true, persona active with its own
        // voice: one unit carries BOTH a StationId and a LeadIn, so the per-kind split is
        // asserted within a single unit rather than across builder variants. The guard airs
        // the first ID at unit 2 (unitCount > 0), so two full units are drained.
        static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts) BuildWithStationIdCadence(
            FakeActivePersonaAccessor accessor, string stationVoice)
        {
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 1,
            };
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", stationVoice));
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var cadenceProvider = new FakeCadenceProvider(cadence);
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
            var catalog = new FakeMediaCatalog(MakeRef("track1"));
            var tts = new FakeTtsSegmentSource();
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts, accessor,
                NullLogger<Orchestrator>.Instance, new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
                new SpeechDeferralQueue(TimeProvider.System),
                TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
            return (orchestrator, tts);
        }

        static async Task<FakeTtsSegmentSource> DrainTwoUnitsAsync(FakeActivePersonaAccessor accessor)
        {
            var (orchestrator, tts) = BuildWithStationIdCadence(accessor, stationVoice: "af_heart");
            var ctx = new PlayoutContext([]);
            // Unit 1 = [LeadIn, Music]; unit 2 = [StationId, LeadIn, Music] — five pulls total.
            for (int i = 0; i < 5; i++)
                Assert.NotNull(await orchestrator.GetNextAsync(ctx, CancellationToken.None));
            return tts;
        }

        [Fact]
        public async Task StationIdRendersWithTheStationVoiceEvenWhileAPersonaIsActive()
        {
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("am_onyx") };

            var tts = await DrainTwoUnitsAsync(accessor);

            var stationId = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Equal("af_heart", stationId.Voice);
        }

        [Fact]
        public async Task StationIdCreditsTheStationNotThePersona()
        {
            // PersonaName null → TtsSegmentSource credits Artist = StationName (gh-#96).
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("am_onyx") };

            var tts = await DrainTwoUnitsAsync(accessor);

            var stationId = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Null(stationId.PersonaName);
        }

        [Fact]
        public async Task LeadInsInTheSameUnitStillCarryThePersonaVoiceAndName()
        {
            // The carve-out must not leak: DJ-spoken kinds keep the F35.3 persona rule.
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("am_onyx") };

            var tts = await DrainTwoUnitsAsync(accessor);

            var leadIns = tts.Requests.Where(r => r.Kind == SegmentKind.LeadIn).ToList();
            Assert.NotEmpty(leadIns);
            Assert.All(leadIns, r => Assert.Equal("am_onyx", r.Voice));
            Assert.All(leadIns, r => Assert.Equal("DJ Nova", r.PersonaName));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — resolution is per-render and non-fatal
    // ---------------------------------------------------------------------

    public sealed class ScenarioAccessorFailureNeverStallsTheCadence
    {
        [Fact]
        public async Task AnAccessorThrowDegradesToStationVoiceAndTheSegmentStillRenders()
        {
            // F12.4 discipline: persona resolution failure must not cost the slot.
            var accessor = new FakeActivePersonaAccessor
            {
                ThrowOnResolve = new InvalidOperationException("persona store unreachable"),
            };
            var (orchestrator, tts) = BuildOrchestrator(accessor, stationVoice: "af_heart");

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.NotNull(tts.LastRequest);
            Assert.Equal("af_heart", tts.LastRequest.Voice);
        }
    }
}
