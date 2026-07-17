// STORY-121 — The active persona flavors copy and voice (voice half)
//
// BDD specification — xUnit. The Orchestrator resolves SegmentRequest.Voice through the
// ActivePersonaAccessor per render — persona voice when non-empty, else Station:Voice.
// Prompt sections are the Tts half (Story121_PersonaPromptSections). Landed T6.
// See docs/PLAN.md Epic T.

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
            NullLogger<Orchestrator>.Instance, new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)));
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
