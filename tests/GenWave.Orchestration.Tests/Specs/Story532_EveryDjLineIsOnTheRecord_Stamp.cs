// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218.1 · PLAN T684)
//
// The planner stamps every persona-spoken request (LeadIn, BackAnnounce, ContextSegment, handoff
// SignOff/SignOn) with persona id, on-air show id and the shift key, so the Tts copy writer can
// record the utterance without any schedule knowledge of its own. See
// tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the recording half.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using GenWave.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureEveryDjLineIsOnTheRecordStamp
{
    const long PersonaId = 10;
    const long ShowId = 5;
    const int StartMinute = 480;

    static readonly DateTimeOffset MondayNineAm = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);
    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static CadenceConfig Cadence() => new()
    {
        LeadInBeforeEachTrack = true,
        BackAnnounceAfterEachTrack = true,
    };

    static ScheduleWeekSnapshot MondayMorningShow() => new(
    [
        new ScheduleSegment(
            Id: 1, Day: DayOfWeek.Monday, StartMinute: StartMinute, EndMinute: 720, PersonaId: PersonaId,
            Genres: null, EnergyMin: null, EnergyMax: null,
            Show: new ShowSummary(ShowId, "Morning Mix", null, null) { Slug = "morning-mix" }, ShowId: ShowId),
    ]);

    // Plans one break that carries a LeadIn, a BackAnnounce, a ContextSegment and a SignOff, and
    // returns the request behind each.
    static async Task<(IReadOnlyDictionary<SegmentKind, SegmentRequest> Requests, DateOnly Today)> PlanAsync(
        ScheduleWeekSnapshot week, bool personaFromSchedule)
    {
        var personaStore = new FakePersonaStore();
        personaStore.Add(TestData.MakePersona(PersonaId, "DJ Alpha", "af_alpha"));

        var contextSettings = new FakeContextSettingsProvider();
        contextSettings.Set("weather", new ContextProviderSettings(true, 10, 5, PersonaId: PersonaId));

        var builder = new BreakPlannerBuilder()
            .WithNow(MondayNineAm)
            .WithSchedule(week)
            .WithPersonaStore(personaStore)
            .WithContextSettings(contextSettings);
        if (!personaFromSchedule)
            builder = builder.WithPersonaAccessor(new FakeActivePersonaAccessor { Persona = TestData.MakePersona(PersonaId, "DJ Alpha", "af_alpha") });

        var chain = builder.Build();
        await chain.ScheduleResolver.ResolveAsync(CancellationToken.None);

        var now = chain.Time.GetUtcNow();
        chain.Queue.Enqueue(
            SpeechDeferralKind.Context, "test: weather due", discriminator: "weather",
            context: new ContextSegmentFacts("Sunny and mild.", now.AddMinutes(30)));
        chain.Queue.Enqueue(
            SpeechDeferralKind.SignOff, "test: arm sign-off",
            handoff: new HandoffContext("af_alpha", "DJ Alpha", "DJ Beta", PersonaId: PersonaId));
        chain.Queue.Enqueue(
            SpeechDeferralKind.SignOn, "test: arm sign-on",
            handoff: new HandoffContext("af_alpha", "DJ Alpha", "DJ Beta", PersonaId: PersonaId));

        var track = TestData.MakeTrackRef("t1").ToMediaItem();
        var breakContext = new BreakContext(1, track, track, "DJ Alpha", Cadence(), Identity, now, null, NoHolds, TimeSpan.Zero);
        var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

        var requests = plan.Slots
            .Where(s => s.Source is RenderSource)
            .GroupBy(s => s.Kind)
            .ToDictionary(g => g.Key, g => ((RenderSource)g.First().Source).Request);
        return (requests, chain.ScheduleResolver.StationToday());
    }

    // The planner stamps what the copy writer records under (SPEC F218.1).
    public sealed class ScenarioAShowIsOnAir : IAsyncLifetime
    {
        IReadOnlyDictionary<SegmentKind, SegmentRequest> requests = new Dictionary<SegmentKind, SegmentRequest>();
        string expectedKey = "";

        public async Task InitializeAsync()
        {
            // Given persona 10 hosting the Monday morning show, at 09:00 on that Monday
            // When  the planner plans a break with a lead-in, back-announce, context segment and sign-off
            var (planned, today) = await PlanAsync(MondayMorningShow(), personaFromSchedule: true);
            requests = planned;
            expectedKey = ShiftKey.Scheduled(PersonaId, DayOfWeek.Monday, StartMinute, today).ToString();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData(SegmentKind.LeadIn)]
        [InlineData(SegmentKind.BackAnnounce)]
        [InlineData(SegmentKind.ContextSegment)]
        [InlineData(SegmentKind.SignOff)]
        [InlineData(SegmentKind.SignOn)]
        public void TheRequestCarriesThePersonaId(SegmentKind kind) =>
            Assert.Equal(PersonaId, requests[kind].PersonaId);

        [Theory]
        [InlineData(SegmentKind.LeadIn)]
        [InlineData(SegmentKind.BackAnnounce)]
        [InlineData(SegmentKind.ContextSegment)]
        [InlineData(SegmentKind.SignOff)]
        public void TheRequestCarriesTheOnAirShowId(SegmentKind kind) =>
            Assert.Equal(ShowId, requests[kind].ShowId);

        [Theory]
        [InlineData(SegmentKind.LeadIn)]
        [InlineData(SegmentKind.BackAnnounce)]
        [InlineData(SegmentKind.ContextSegment)]
        [InlineData(SegmentKind.SignOff)]
        public void TheRequestCarriesAShiftKeyBuiltFromStationToday(SegmentKind kind) =>
            Assert.Equal(expectedKey, requests[kind].ShiftKeyText);
    }

    // A gap in the schedule is still a shift: the key is the persona's day, and there is no show.
    public sealed class ScenarioNoSegmentIsOnAir : IAsyncLifetime
    {
        IReadOnlyDictionary<SegmentKind, SegmentRequest> requests = new Dictionary<SegmentKind, SegmentRequest>();
        string expectedKey = "";

        public async Task InitializeAsync()
        {
            // Given persona 10 active with nothing scheduled
            // When  the planner plans the same break
            var (planned, today) = await PlanAsync(new ScheduleWeekSnapshot([]), personaFromSchedule: false);
            requests = planned;
            expectedKey = ShiftKey.Unscheduled(PersonaId, today).ToString();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData(SegmentKind.LeadIn)]
        [InlineData(SegmentKind.BackAnnounce)]
        [InlineData(SegmentKind.ContextSegment)]
        [InlineData(SegmentKind.SignOff)]
        public void TheShowIdIsNull(SegmentKind kind) => Assert.Null(requests[kind].ShowId);

        [Theory]
        [InlineData(SegmentKind.LeadIn)]
        [InlineData(SegmentKind.BackAnnounce)]
        [InlineData(SegmentKind.ContextSegment)]
        [InlineData(SegmentKind.SignOff)]
        public void TheShiftKeyIsUnscheduled(SegmentKind kind) =>
            Assert.Equal(expectedKey, requests[kind].ShiftKeyText);
    }
}
