// STORY-138 — Station identity goes live (Epic V / SPEC F44.1, closes gitea-#196) — Orchestrator half.
// The Host-level facts (allowlist, /api/stations liveness, StationContext-gone, blank-name
// rejection, voice liveness) live in Host.Tests/Specs/Story138_StationIdentityLive.cs.
//
// This file owns AC2's Orchestrator-side observation — SegmentRequest.StationName carries a
// live-edited Station:Name — split into two facts (baseline + live-edit), mirroring Story135's
// TheProviderDepthIsPassedToEverySelection/AChangedDepthAppliesOnTheVeryNextSelection pair (the
// Story131 split precedent: facts live where their subject compiles — Orchestrator is
// Orchestration-project code, so this fact moved out of Host.Tests' pending file).
//
// BDD specification — xUnit. Implemented V7 (2026-07-14).

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStationIdentityLive
{
    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    // LeadIn-only cadence: exactly one segment render per unit, so LastRequest unambiguously
    // reflects the request this fact is asserting on (mirrors Story121/Story131's own helper).
    static CadenceConfig LeadInOnlyCadence => new()
    {
        LeadInBeforeEachTrack = true,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts, FakeStationIdentityProvider Identity)
        BuildOrchestrator(string name)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", name, "af_heart"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(LeadInOnlyCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeRef("track1"));
        var tts = new FakeTtsSegmentSource();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts,
            new FakeActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)));
        return (orchestrator, tts, identityProvider);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNameIsLiveTunable
    {
        [Fact]
        public async Task TheSegmentRequestCarriesTheCurrentStationName()
        {
            var (orchestrator, tts, _) = BuildOrchestrator("GenWave");

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal("GenWave", tts.LastRequest?.StationName);
        }

        [Fact]
        public async Task AChangedNameAppliesOnTheVeryNextSegment()
        {
            var (orchestrator, tts, identity) = BuildOrchestrator("GenWave");
            var ctx = new PlayoutContext([]);

            // A LeadIn-only unit is TWO buffered items ([LeadIn segment, Music track]) — fully
            // drain unit 1 (two GetNextAsync calls) before mutating identity, so the live edit
            // below lands strictly BETWEEN units, never mid-unit (mirrors the cadence-read-once-
            // per-unit discipline this same provider follows).
            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 1: LeadIn segment
            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 1: Music track
            Assert.Equal("GenWave", tts.LastRequest?.StationName);

            // The live edit: no re-construction, no restart — same provider instance, new value
            // (SPEC F44.1, gitea-#196, mirrors Story135's rotation-depth live edit one seam over).
            identity.Identity = identity.Identity with { Name = "Radio Free Somewhere" };

            // Unit 2: the buffer is empty again, so this call triggers a NEW EnqueuePatterAsync —
            // the very next segment request carries the edited name.
            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 2: LeadIn segment
            Assert.Equal("Radio Free Somewhere", tts.LastRequest?.StationName);
        }
    }
}
