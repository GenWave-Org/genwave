// STORY-228 — The DJ tips their hat, in their own words only (SPEC F87.7, PLAN T91)
//
// BDD specification — xUnit. The Tts-side prompt/template/reflection facts live in
// GenWave.Tts.Tests/Specs/Story228_RequestShoutOut.cs (Tts.Tests has no ProjectReference to
// GenWave.Orchestration). These two facts are Orchestration-side because they depend on driving the
// real Orchestrator: the marker actually rides the SAME SegmentRequest.Track a fulfilled pick's own
// lead-in gets built with (T90's carry-through, PLAN T91 review — proven live here rather than
// re-asserted against a hand-built SegmentRequest), and cadence still gates whether a lead-in is
// built at all — a fulfilled track never manufactures an orphan lead-in cadence wouldn't otherwise
// enqueue (extends FeatureOrchestratorPlanner's own cadence idiom, Story007_OrchestratorPlanner.cs).

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureRequestShoutOutRidesTheLeadIn
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static MediaReference MakeRequestedRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: "The Requesters",
        Album: null,
        Genre: null,
        Year: null);

    /// <summary>
    /// An Orchestrator whose every pick is short-circuited by a scripted fulfilled request (SPEC
    /// F87.6, mirrors Story227's own FeatureOrchestratorConsultationOrder.BuildOrchestrator) — an
    /// empty catalog means the only way a pick ever reaches air is through this fulfillment rung.
    /// </summary>
    static Orchestrator BuildOrchestrator(CadenceConfig cadence, ITtsSegmentSource ttsSource)
    {
        var fulfilled = new RotationCandidate(MakeRequestedRef("requested"), false, false, RequestFulfilled: true);
        var fulfillmentSource = new FakeRequestFulfillmentSource
        {
            NextResult = new RequestFulfillment(fulfilled, RequestId: 42, WasVibe: false),
        };
        var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);

        return new Orchestrator(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new FakeMediaCatalog(null),
            ttsSource,
            new FakeActivePersonaAccessor(),
            new CapturingLogger<Orchestrator>(),
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System,
            new FakeBoundaryBiasProvider(TimeSpan.Zero),
            new FakeEnvelopeProvider(envelope),
            personaPickProvider: null,
            requestFulfillmentSource: fulfillmentSource);
    }

    static CadenceConfig CadenceWithLeadIn(bool leadInBeforeEachTrack) => new()
    {
        LeadInBeforeEachTrack = leadInBeforeEachTrack,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    public static class ScenarioLeadInCadenceOn
    {
        [Fact]
        public static async Task TheShoutOutRidesTheFulfilledTracksOwnLeadIn()
        {
            var ttsSource = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(CadenceWithLeadIn(leadInBeforeEachTrack: true), ttsSource);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the SAME SegmentRequest built for this track's lead-in carries the marker — not a
            // second, independently-stamped flag (SPEC F87.6/F87.7).
            var leadIn = Assert.Single(ttsSource.Requests, r => r.Kind == SegmentKind.LeadIn);
            Assert.True(leadIn.Track?.RequestFulfilled);
        }
    }

    public static class ScenarioLeadInCadenceOff
    {
        [Fact]
        public static async Task CadenceOffMeansNoOrphanAcknowledgmentSegment()
        {
            var ttsSource = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(CadenceWithLeadIn(leadInBeforeEachTrack: false), ttsSource);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then no lead-in is built at all, fulfilled track or not — the flag colors an existing
            // lead-in when cadence would already enqueue one; it never manufactures one of its own.
            Assert.DoesNotContain(ttsSource.Requests, r => r.Kind == SegmentKind.LeadIn);
        }
    }
}
