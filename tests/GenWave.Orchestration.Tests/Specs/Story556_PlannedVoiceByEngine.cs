// STORY-556 — Every speaker keeps a voice per engine, the planner half (SPEC F219.9/.11, F189.5 · PLAN T753)
//
// The planner stamps the voice the snapshot source resolved for the active engine, keeps the validated row voice
// when the active engine owns the card's voice, and carries VoiceMissing so the render drops the talk. A handoff
// drained after an engine switch re-reads only the voice of its armed snapshot.

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using GenWave.TestSupport;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePlannedVoiceByEngine
{
    const long PersonaId = 10;

    static readonly DateTimeOffset Now = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static SpeakerSnapshot Snapshot(string voice, bool overridesRow = false, bool missing = false, string hash = "fresh-hash", double pace = 1.0) =>
        new(PersonaId, "DJ Alpha", voice, pace, [], [], hash) { VoiceOverridesRow = overridesRow, VoiceMissing = missing };

    // Persona snapshots by id; the station snapshot is the identity's voice, missing when the identity says so.
    sealed class ScriptedSnapshotSource(SpeakerSnapshot? persona) : ISpeakerSnapshotSource
    {
        public Task<SpeakerSnapshot> ForStationAsync(StationIdentity identity, CancellationToken ct) =>
            Task.FromResult(new SpeakerSnapshot(null, null, identity.Voice, 1.0, [], [], "station") { VoiceMissing = identity.VoiceMissing });

        public Task<SpeakerSnapshot> ForPersonaAsync(long personaId, CancellationToken ct) =>
            Task.FromResult(persona ?? throw new InvalidOperationException("no persona snapshot scripted"));
    }

    static async Task<BreakPlan> PlanAsync(
        ISpeakerSnapshotSource source, StationIdentity identity, Action<BreakPlannerChain>? arm = null,
        IAnnouncementSource? announcements = null, ITtsVoiceLister? voiceLister = null)
    {
        var personaStore = new FakePersonaStore();
        personaStore.Add(TestData.MakePersona(PersonaId, "DJ Alpha", "af_alpha"));
        var builder = new BreakPlannerBuilder()
            .WithNow(Now)
            .WithPersonaStore(personaStore)
            .WithPersonaAccessor(new FakeActivePersonaAccessor { Persona = TestData.MakePersona(PersonaId, "DJ Alpha", "af_alpha") })
            .WithSpeakerSnapshotSource(source)
            .WithAnnouncementSource(announcements)
            .WithVoiceLister(voiceLister);
        var chain = builder.Build();
        arm?.Invoke(chain);

        var track = TestData.MakeTrackRef("t1").ToMediaItem();
        var cadence = new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = false };
        return await chain.Planner.PlanAsync(
            new BreakContext(1, track, track, "DJ Alpha", cadence, identity, Now, null, NoHolds, TimeSpan.Zero), CancellationToken.None);
    }

    static SegmentRequest RequestOf(BreakPlan plan, SegmentKind kind) =>
        ((RenderSource)Assert.Single(plan.Slots, s => s.Kind == kind && s.Source is RenderSource).Source).Request;

    static readonly StationIdentity Station = new("station-1", "GenWave", "voice-station");

    public sealed class ScenarioTheActiveEngineIsNotTheCardsOwn
    {
        readonly Task<BreakPlan> plan = PlanAsync(new ScriptedSnapshotSource(Snapshot("reader-1", overridesRow: true)), Station);

        [Fact]
        public async Task TheLeadInIsStampedWithTheEnginesVoice() =>
            Assert.Equal("reader-1", RequestOf(await plan, SegmentKind.LeadIn).Voice);

        [Fact]
        public async Task TheSnapshotCarriesTheSameVoice() =>
            Assert.Equal("reader-1", RequestOf(await plan, SegmentKind.LeadIn).Speaker?.Voice);
    }

    // Ruling 9: a card voice that never passed the import check never overrides the validated row voice
    public sealed class ScenarioTheActiveEngineOwnsTheCardsVoice
    {
        readonly Task<BreakPlan> plan = PlanAsync(new ScriptedSnapshotSource(Snapshot("not-installed")), Station);

        [Fact]
        public async Task TheValidatedRowVoiceStands() =>
            Assert.Equal("af_alpha", RequestOf(await plan, SegmentKind.LeadIn).Voice);
    }

    // Sad path
    public sealed class ScenarioTheDjHasNoVoiceForTheActiveEngine
    {
        readonly Task<BreakPlan> plan = PlanAsync(new ScriptedSnapshotSource(Snapshot(string.Empty, missing: true)), Station);

        [Fact]
        public async Task TheRequestCarriesVoiceMissing() =>
            Assert.True(RequestOf(await plan, SegmentKind.LeadIn).Speaker?.VoiceMissing);
    }

    // F189.5 — the handoff was armed on kokoro; the engine is chatterbox when it drains
    public sealed class ScenarioTheEngineSwitchesBetweenArmAndDrain
    {
        static readonly SpeakerSnapshot Armed = Snapshot("af_alpha", hash: "armed-hash", pace: 1.3);

        readonly Task<BreakPlan> plan = PlanAsync(
            new ScriptedSnapshotSource(Snapshot("reader-1", overridesRow: true)),
            Station,
            chain => chain.Queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: arm sign-off",
                handoff: new HandoffContext("af_alpha", "DJ Alpha", "DJ Beta", Speaker: Armed, PersonaId: PersonaId)));

        [Fact]
        public async Task TheHandoffVoiceFollowsTheEngine() =>
            Assert.Equal("reader-1", RequestOf(await plan, SegmentKind.SignOff).Voice);

        [Fact]
        public async Task TheArmedSnapshotCarriesTheFreshVoice() =>
            Assert.Equal("reader-1", RequestOf(await plan, SegmentKind.SignOff).Speaker?.Voice);

        [Fact]
        public async Task TheArmedContentHashStays() =>
            Assert.Equal("armed-hash", RequestOf(await plan, SegmentKind.SignOff).Speaker?.ContentHash);

        [Fact]
        public async Task TheArmedPaceStays() =>
            Assert.Equal(1.3, RequestOf(await plan, SegmentKind.SignOff).Speaker?.Pace);
    }

    // Sad path
    public sealed class ScenarioTheArmedDjLosesItsVoiceBeforeTheDrain
    {
        readonly Task<BreakPlan> plan = PlanAsync(
            new ScriptedSnapshotSource(Snapshot(string.Empty, missing: true)),
            Station,
            chain => chain.Queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: arm sign-off",
                handoff: new HandoffContext("af_alpha", "DJ Alpha", "DJ Beta", Speaker: Snapshot("af_alpha"), PersonaId: PersonaId)));

        [Fact]
        public async Task TheHandoffIsMarkedMissing() =>
            Assert.True(RequestOf(await plan, SegmentKind.SignOff).Speaker?.VoiceMissing);
    }

    // A station voice with no entry for the engine must not silence an announcement that names an installed voice
    static async Task<SegmentRequest> PlanAnnouncementOnAStationWithoutAVoiceAsync(string requestedVoice)
    {
        var announcements = new FakeAnnouncementSource();
        announcements.Pending.Enqueue(new AnnouncementItem(1, "Lost dog on Elm.", Verbatim: true, requestedVoice));
        var plan = await PlanAsync(
            new ScriptedSnapshotSource(Snapshot("af_alpha")),
            Station with { Voice = string.Empty, VoiceMissing = true },
            announcements: announcements,
            voiceLister: new FakeTtsVoiceLister { KnownVoices = ["announcer-1"] });
        return ((VerbatimSource)Assert.Single(plan.Slots, s => s.Kind == SegmentKind.Announcement).Source).Request;
    }

    public sealed class ScenarioAnAnnouncementNamesAnInstalledVoice
    {
        readonly Task<SegmentRequest> request = PlanAnnouncementOnAStationWithoutAVoiceAsync("announcer-1");

        [Fact]
        public async Task ItIsNotMarkedMissing() => Assert.False((await request).Speaker?.VoiceMissing);

        [Fact]
        public async Task ItKeepsTheRequestedVoice() => Assert.Equal("announcer-1", (await request).Voice);
    }

    // Sad path
    public sealed class ScenarioAnAnnouncementFallsBackToAStationVoiceThatIsMissing
    {
        readonly Task<SegmentRequest> request = PlanAnnouncementOnAStationWithoutAVoiceAsync("not-installed");

        [Fact]
        public async Task ItIsMarkedMissing() => Assert.True((await request).Speaker?.VoiceMissing);
    }
}
