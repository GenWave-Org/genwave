// STORY-536 AC2 (handoff half) — the planner stamps both personas of a handoff on the request (SPEC F218.15(b) · PLAN T726)
//
// The memory consumer writes one event and an episode per side from the aired item alone, so the SignOff and
// SignOn requests each carry who handed over to whom. A same-persona transition (no counterpart) carries none.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using GenWave.TestSupport;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAHandoffNamesBothSides
{
    static readonly DateTimeOffset MondayNineAm = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Boundary = MondayNineAm.AddMinutes(30);
    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");

    static async Task<IReadOnlyDictionary<SegmentKind, SegmentRequest>> PlanAsync(long? counterpartId, string? counterpartName)
    {
        var personaStore = new FakePersonaStore();
        personaStore.Add(TestData.MakePersona(10, "Dusty", "af_alpha"));
        var chain = new BreakPlannerBuilder()
            .WithNow(MondayNineAm)
            .WithPersonaStore(personaStore)
            .WithPersonaAccessor(new FakeActivePersonaAccessor { Persona = TestData.MakePersona(10, "Dusty", "af_alpha") })
            .Build();
        await chain.ScheduleResolver.ResolveAsync(CancellationToken.None);

        chain.Queue.Enqueue(
            SpeechDeferralKind.SignOff, "test: arm sign-off",
            handoff: new HandoffContext("af_alpha", "Dusty", counterpartName, PersonaId: 10, CounterpartPersonaId: counterpartId, BoundaryAt: Boundary));
        chain.Queue.Enqueue(
            SpeechDeferralKind.SignOn, "test: arm sign-on",
            handoff: new HandoffContext("af_beta", "Flip", "Dusty", PersonaId: counterpartId, CounterpartPersonaId: 10, BoundaryAt: Boundary));

        var track = TestData.MakeTrackRef("t1").ToMediaItem();
        var context = new BreakContext(
            1, track, track, "Dusty", new CadenceConfig(), Identity, chain.Time.GetUtcNow(), null, new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
        var plan = await chain.Planner.PlanAsync(context, CancellationToken.None);

        return plan.Slots
            .Where(s => s.Source is RenderSource)
            .GroupBy(s => s.Kind)
            .ToDictionary(g => g.Key, g => ((RenderSource)g.First().Source).Request);
    }

    public sealed class ScenarioTwoDistinctPersonasHandOver : IAsyncLifetime
    {
        IReadOnlyDictionary<SegmentKind, SegmentRequest> requests = new Dictionary<SegmentKind, SegmentRequest>();

        public async Task InitializeAsync() => requests = await PlanAsync(counterpartId: 20, counterpartName: "Flip");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSignOffNamesWhoHandedOverToWhom() =>
            Assert.Equal(
                new HandoffParticipants(new CrosstalkParticipant(10, "Dusty"), new CrosstalkParticipant(20, "Flip"), Boundary),
                requests[SegmentKind.SignOff].Handoff);

        [Fact]
        public void TheSignOnNamesTheSamePair() =>
            Assert.Equal(requests[SegmentKind.SignOff].Handoff, requests[SegmentKind.SignOn].Handoff);
    }

    public sealed class ScenarioThereIsNoCounterpart : IAsyncLifetime
    {
        IReadOnlyDictionary<SegmentKind, SegmentRequest> requests = new Dictionary<SegmentKind, SegmentRequest>();

        public async Task InitializeAsync() => requests = await PlanAsync(counterpartId: null, counterpartName: null);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSignOffCarriesNoHandoff() => Assert.Null(requests[SegmentKind.SignOff].Handoff);
    }
}
