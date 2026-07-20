// STORY-131 — Patter airs credited to the active DJ, on every surface (Epic U / SPEC F39,
// closes gitea-#212) — request-shape half. The artist-stamping half lives in
// Tts.Tests/Specs/Story131_PersonaAttributionArtistStamping.cs (the STORY-117/121 split:
// facts live where their subject compiles, one story notwithstanding).
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-13, house rule since Epic S).
// Implemented U4 (2026-07-13).

namespace GenWave.Orchestration.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeaturePersonaAttributionRequestShape
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

    static Persona BuildPersona(string name, string voice) =>
        new(1, name, "", "", voice, DateTime.UtcNow, DateTime.UtcNow);

    // LeadIn-only cadence: exactly one segment render per unit, so LastRequest unambiguously
    // reflects the request this fact is asserting on (mirrors Story121_PersonaVoiceResolution).
    static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts) BuildOrchestrator(
        IActivePersonaAccessor accessor, string stationVoice = "af_heart")
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
            new SpeechDeferralQueue(TimeProvider.System));
        return (orchestrator, tts);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — name and voice come from one accessor read
    // ---------------------------------------------------------------------

    public sealed class ScenarioNameAndVoiceComeFromOneAccessorRead
    {
        [Fact]
        public async Task AnActivePersonaStampsItsNameOnTheSegmentRequest()
        {
            // F39.1 — the active persona's name rides the SegmentRequest.
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("DJ Nova", "am_onyx") };
            var (orchestrator, tts) = BuildOrchestrator(accessor);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal("DJ Nova", tts.LastRequest?.PersonaName);
        }

        [Fact]
        public async Task TheVoiceAndTheNameOnOneRequestAlwaysDescribeTheSamePersona()
        {
            // F39.1 — Voice and PersonaName are read together, so they never disagree about which
            // persona is on air.
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("DJ Nova", "am_onyx") };
            var (orchestrator, tts) = BuildOrchestrator(accessor);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal(("am_onyx", "DJ Nova"), (tts.LastRequest?.Voice, tts.LastRequest?.PersonaName));
        }

        [Fact]
        public async Task TheAccessorIsReadExactlyOncePerSegmentRequest()
        {
            // F39.1 — one SegmentRequest build must cost exactly one IActivePersonaAccessor.ResolveAsync
            // call, never a separate call for Voice and another for PersonaName.
            var accessor = new CountingActivePersonaAccessor(BuildPersona("DJ Nova", "am_onyx"));
            var (orchestrator, _) = BuildOrchestrator(accessor);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal(1, accessor.CallCount);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioNoActivePersonaLeavesTheRequestUnchanged
    {
        [Fact]
        public async Task PersonaNameIsNullWhenNoPersonaIsActive()
        {
            // F39.2 — no active persona means PersonaName stays null.
            var accessor = new FakeActivePersonaAccessor(); // Persona stays null
            var (orchestrator, tts) = BuildOrchestrator(accessor);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Null(tts.LastRequest?.PersonaName);
        }

        [Fact]
        public async Task VoiceFallsBackToTheStationVoiceWhenNoPersonaIsActive()
        {
            // F39.2 — byte-identical to pre-F39 behavior: Voice still falls back to Station:Voice.
            var accessor = new FakeActivePersonaAccessor(); // Persona stays null
            var (orchestrator, tts) = BuildOrchestrator(accessor, stationVoice: "af_heart");

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal("af_heart", tts.LastRequest?.Voice);
        }
    }
}

/// <summary>
/// Counts <see cref="IActivePersonaAccessor.ResolveAsync"/> invocations — proves the Orchestrator
/// resolves Voice and PersonaName from a SINGLE read per <see cref="SegmentRequest"/> build (F39.1),
/// never two separate accessor calls. File-scoped: used only by this spec (the repo's fake idiom for
/// a single-file double, e.g. Story095_StationNamePlaceholderExpansion.cs in Host.Tests).
/// </summary>
file sealed class CountingActivePersonaAccessor(Persona? persona) : IActivePersonaAccessor
{
    public int CallCount { get; private set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(persona);
    }
}
