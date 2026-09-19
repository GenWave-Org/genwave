// STORY-452 — The break characterisation replay (gh-#401 · SPEC F185 · PLAN T515, T522)
//
// BDD specification — xUnit. FROZEN for gh-#401 (PLAN T515): AC1–AC4 pin four tables — buffer
// order by media id, DjName per item, event kinds in order, and WARN+ messages — each OBSERVED
// from ONE deterministic run of the F185.1 script through OrchestratorBuilder against the real,
// unsplit Orchestrator (RunScriptAsync below). Once T515 lands, no PR inside gh-#401 may change an
// assertion in THIS file or in Story452_PinnedTables.cs. The ONLY exception is PLAN T522 (PR-3),
// which may un-skip AC6 (MatchThePinnedTraceTable) and add BreakPlan.ToTrace() once BreakPlan
// exists. Any other change to this pair must name the behaviour change in its own PR title and in
// SPEC F186.
//
// One script, served unit by unit on a shared FakeTimeProvider: back-announce + lead-in on,
// station-id cadence hit, ad cadence hit, two pending owner announcements, a vended crosstalk
// exchange, a due context segment, time/date at on-time/late/expired, a show boundary with a
// straddling track (sign-off drains, sign-on holds), a ceremony-only finale, one over-budget TTS
// render (dropped), one null TTS render (dropped).
//
// Disclosed deviations from the SPEC F185.1 "six music units" headline:
//   - Nine units, not six: the over-budget render and the null render each get their own isolated
//     unit (every other cadence off) so neither drop is masked by an unrelated element sharing the
//     pull; the ceremony-only finale is its own unit by construction (SPEC F124/gh-#300).
//   - The crosstalk asset lives at a FIXED path this file writes before the run, not STORY-329's
//     Path.GetTempFileName() idiom — a random name would leak into the crosstalk item's MediaId
//     (tts:crosstalk:{asset filename}), breaking AC1's determinism across runs.
//   - The "over-budget render" beat is a sign-off, not an owner announcement: the announcement
//     renderer double completes synchronously with no delay mechanism to race a budget against,
//     while FakeTtsSegmentSource.RenderDelay does.

using System.Runtime.CompilerServices;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakCharacterisationReplay
{
    const long HostPersonaId = 10;
    const long NeighborPersonaId = 20;

    /// <summary>The one handoff pairing every ceremony beat (O5, O6, O9) hands off across.</summary>
    static readonly HandoffContext Handoff = new("af_flip", "Nova", "Milo");

    /// <summary>
    /// Everything AC1–AC4 assert on, captured from one deterministic run of the script, plus AC6's
    /// per-unit <see cref="BreakPlan.ToTrace"/> lines (PLAN T522). Internal, not private — Story455's
    /// own AC5 facts (<c>ScenarioTheOrchestratorRidesThePlan</c>) call <see cref="RunScriptAsync"/>
    /// directly rather than duplicating this script.
    /// </summary>
    internal sealed record ReplayResult(
        IReadOnlyList<string> BufferedMediaIds,
        IReadOnlyList<string> DjNames,
        IReadOnlyList<string> EventKinds,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Traces);

    /// <summary>
    /// Builds the F185.1 script through <see cref="OrchestratorBuilder"/> against the real, unsplit
    /// <see cref="Orchestrator"/> and serves it unit by unit — see this file's header for the
    /// nine-vs-six-units disclosure.
    /// </summary>
    internal static async Task<ReplayResult> RunScriptAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
        var cadence = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = true,
            BackAnnounceAfterEachTrack = true,
            StationIdEveryNUnits = 3,
        });
        var adCadence = new FakeAdCadenceProvider(2);
        var adSpotVend = new FakeAdSpotVend();
        var announcementSource = new FakeAnnouncementSource();
        var announcementRenderer = new FakeVerbatimSegmentRenderer();
        var fakeTts = new FakeTtsSegmentSource { TimeProvider = clock };
        var events = new CapturingStationEventSink();
        var logger = new CapturingLogger<Orchestrator>();
        var planObserver = new CapturingBreakPlanObserver();

        var personaStore = new FakePersonaStore();
        personaStore.Add(TestData.MakePersona(HostPersonaId, "Nova", "af_nova"));
        personaStore.Add(TestData.MakePersona(NeighborPersonaId, "Milo", "af_milo"));

        var week = new ScheduleWeekSnapshot([
            new ScheduleSegment(1, DayOfWeek.Monday, 0, 720, HostPersonaId, null, null, null,
                Show: new ShowSummary(5, "Morning Mix", null, null) { Slug = "morning-mix" }, ShowId: 5),
            new ScheduleSegment(2, DayOfWeek.Monday, 720, 1440, NeighborPersonaId, null, null, null),
        ]);

        var crosstalkScope = new FakeCrosstalkScopeProvider(["morning-mix"], everyNthAiring: 1);
        var crosstalkPlanner = new CrosstalkPlanner(personaStore, crosstalkScope, NullLogger<CrosstalkPlanner>.Instance);
        var crosstalkAssetPath = Path.Combine(Path.GetTempPath(), "genwave-story452-crosstalk.wav");
        File.WriteAllBytes(crosstalkAssetPath, [0]);

        var contextSettings = new FakeContextSettingsProvider();
        var imaging = new FakeStationImagingSettingsProvider { Current = new StationImagingSettings(false, false, 180) };

        var catalog = FakeMediaCatalog.WithPool([
            TestData.MakeTrackRef("t1"), TestData.MakeTrackRef("t2"), TestData.MakeTrackRef("t3"),
            TestData.MakeTrackRef("t4"), TestData.MakeTrackRef("t5"),
            TestData.MakeTrackRef("t6-straddle") with { DurationMs = 540_000 },
        ]);

        var chain = new OrchestratorBuilder()
            .WithTime(clock)
            .WithSchedule(week)
            .WithPersonaStore(personaStore)
            .WithCadence(cadence)
            .WithAdCadence(adCadence)
            .WithAdSpotVend(adSpotVend)
            .WithAnnouncementSource(announcementSource)
            .WithAnnouncementRenderer(announcementRenderer)
            .WithCrosstalkPlanner(crosstalkPlanner)
            .WithContextSettings(contextSettings)
            .WithImagingSettings(imaging)
            .WithCatalog(catalog)
            .WithTts(fakeTts)
            .WithEvents(events)
            .WithLogger(logger)
            .WithLookahead(TimeSpan.FromMinutes(10))
            .WithPlanObserver(planObserver)
            .Build();

        var orchestrator = chain.Orchestrator;
        var queue = chain.Queue;

        async Task<MediaItem> PullOneAsync(PlayoutContext? ctx = null)
        {
            var item = await orchestrator.GetNextAsync(ctx ?? new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(item);
            return item;
        }

        async Task<List<MediaItem>> PullManyAsync(int count, PlayoutContext? ctx = null)
        {
            var items = new List<MediaItem>();
            for (var i = 0; i < count; i++) items.Add(await PullOneAsync(ctx));
            return items;
        }

        // Races the sign-off render (RenderDelay=10s) against the default 5s render budget by
        // advancing the SAME clock the budget's own timer and the render's own Task.Delay share
        // (mirrors STORY-243/STORY-442's own racing idiom).
        async Task<MediaItem> PullRacingBudgetAsync()
        {
            var task = orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            for (var i = 0; i < 8 && !task.IsCompleted; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Yield();
            }

            var item = await task;
            Assert.NotNull(item);
            return item;
        }

        // O1 — two pending owner announcements drain (SPEC F144.1) alongside the opening lead-in;
        // no back-announce yet (no previous track).
        announcementSource.Pending.Enqueue(new AnnouncementItem(9001, "Happy birthday to Sam!", Verbatim: true, RequestedVoice: null));
        announcementSource.Pending.Enqueue(new AnnouncementItem(9002, "Pledge drive ends Friday.", Verbatim: true, RequestedVoice: null));
        var o1 = await PullManyAsync(4);

        // O2 — a stocked crosstalk exchange vends for the enabled "morning-mix" show (SPEC F127.1).
        crosstalkPlanner.Stock(new StockedCrosstalkExchange("morning-mix",
            new CrosstalkCast(HostPersonaId, NeighborPersonaId), crosstalkAssetPath, new Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 6_000));
        var o2 = await PullManyAsync(4);

        // O3 — the ad cadence (every 2 units) hits (SPEC F158.1).
        adSpotVend.Answer = new MediaItem("ad-spot-1", "/ads/spot1.mp3", "Ad Spot", new Loudness(-16.0, -1.0, true));
        var o3 = await PullManyAsync(4);

        // O4 — the station-id cadence (every 3 units) hits alongside a due, fresh context segment
        // (SPEC F107.3).
        queue.Enqueue(SpeechDeferralKind.Context, "test: weather due", due: clock.GetUtcNow(), discriminator: "weather",
            context: new ContextSegmentFacts("Sunny and mild, 70F.", clock.GetUtcNow().AddMinutes(10)));
        var o4 = await PullManyAsync(5);

        // O5 — an isolated sign-off whose render exceeds the render budget and is dropped (SPEC
        // F92.4). Every other cadence is off for this one unit so the sign-off is the ONLY pending
        // render; the ad cadence is also permanently zeroed here — its one hit at O3 already proved
        // the element, and leaving it on would refire later and collide with O7/O8's own design.
        cadence.Cadence = new CadenceConfig { LeadInBeforeEachTrack = false, BackAnnounceAfterEachTrack = false, StationIdEveryNUnits = 0 };
        adCadence.EveryNUnits = 0;
        fakeTts.RenderDelay = TimeSpan.FromSeconds(10);
        queue.Enqueue(SpeechDeferralKind.SignOff, "test: over-budget signoff", due: clock.GetUtcNow(), handoff: Handoff);
        var o5 = new List<MediaItem> { await PullRacingBudgetAsync() };
        fakeTts.RenderDelay = null;

        // O6 — a show-boundary straddle: the sign-off drains now (due in 6 minutes, inside the
        // 10-minute lookahead), the paired sign-on holds, and the boundary-crossing 9-minute track
        // straddles it (SPEC F111/F124). Lead-in/back-announce return; station-id stays off for the
        // rest of the script — its one hit at O4 already proved the element.
        cadence.Cadence = new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = true, StationIdEveryNUnits = 0 };
        var signOffDue = clock.GetUtcNow() + TimeSpan.FromMinutes(6);
        queue.Enqueue(SpeechDeferralKind.SignOff, "test: straddle signoff", due: signOffDue, handoff: Handoff);
        queue.Enqueue(SpeechDeferralKind.SignOn, "test: straddle signon", due: signOffDue + TimeSpan.FromSeconds(15), handoff: Handoff);
        var o6 = await PullManyAsync(4);

        // O7 — the held sign-on drains once its due time passes, alongside a context segment whose
        // render returns null and is dropped (SPEC F107.6).
        clock.Advance(TimeSpan.FromMinutes(7));
        fakeTts.ShouldReturnNull = request => request.Kind == SegmentKind.ContextSegment;
        queue.Enqueue(SpeechDeferralKind.Context, "test: null-render context", due: clock.GetUtcNow(), discriminator: "traffic",
            context: new ContextSegmentFacts("Heavy traffic downtown.", clock.GetUtcNow().AddMinutes(10)));
        var o7 = await PullManyAsync(4);
        fakeTts.ShouldReturnNull = null;

        // O8 — three time/date deferrals with distinct discriminators (so none supersedes another —
        // SpeechDeferralQueue's own (Kind, Discriminator) key): on-time, late (past the 90s honesty
        // threshold but inside the 180s budget), and expired (past the budget — dropped undrained,
        // SPEC F141.2).
        var timeDateNow = clock.GetUtcNow();
        queue.Enqueue(SpeechDeferralKind.TimeDate, "test: on-time", due: timeDateNow, discriminator: "ontime");
        queue.Enqueue(SpeechDeferralKind.TimeDate, "test: late", due: timeDateNow - TimeSpan.FromSeconds(120), discriminator: "late");
        queue.Enqueue(SpeechDeferralKind.TimeDate, "test: expired", due: timeDateNow - TimeSpan.FromSeconds(300), discriminator: "expired");
        var o8 = await PullManyAsync(5);

        // O9 — a ceremony-only finale (SPEC F124/gh-#300): with lead-in/back-announce off (matching
        // gh-#300's own arrange — a pending back-announce would otherwise air INSTEAD of the
        // ceremony), the queued-ahead tail already crosses the 45s-out boundary, so the decline
        // check fires before any music is planned: a single spoken segment (sign-off; the paired
        // sign-on holds), drawing nothing from the catalog.
        cadence.Cadence = new CadenceConfig { LeadInBeforeEachTrack = false, BackAnnounceAfterEachTrack = false, StationIdEveryNUnits = 0 };
        queue.Enqueue(SpeechDeferralKind.SignOff, "test: decline signoff", due: clock.GetUtcNow() + TimeSpan.FromSeconds(30), handoff: Handoff);
        queue.Enqueue(SpeechDeferralKind.SignOn, "test: decline signon", due: clock.GetUtcNow() + TimeSpan.FromSeconds(45), handoff: Handoff);
        var o9 = await PullManyAsync(1, new PlayoutContext([], QueuedAheadMs: 200_000));

        List<MediaItem>[] units = [o1, o2, o3, o4, o5, o6, o7, o8, o9];
        return new ReplayResult(
            [.. units.SelectMany(unit => unit.Select(item => item.MediaId))],
            [.. units.SelectMany(unit => unit.Select(item => item.DjName ?? "(none)"))],
            [.. events.Events.Select(evt => evt.GetType().Name)],
            [.. logger.Warnings],
            [.. planObserver.Plans.Select(plan => plan.ToTrace())]);
    }

    public sealed class ScenarioTheScriptServedEndToEnd
    {
        // Given: the F185.1 script through the builder, every unit served (RunScriptAsync above)

        /// <summary>AC1 — media ids per unit equal the pinned table.</summary>
        [Fact]
        public async Task BuffersTheUnitsInThePinnedOrder()
        {
            var result = await RunScriptAsync();

            Assert.Equal(Story452PinnedTables.BufferedMediaIds, result.BufferedMediaIds);
        }

        /// <summary>AC2 — DjName per buffered item equals the pinned table.</summary>
        [Fact]
        public async Task StampsThePinnedDjNames()
        {
            var result = await RunScriptAsync();

            Assert.Equal(Story452PinnedTables.DjNames, result.DjNames);
        }

        /// <summary>AC3 — event kinds in order equal the pinned list.</summary>
        [Fact]
        public async Task PublishesThePinnedEventKinds()
        {
            var result = await RunScriptAsync();

            Assert.Equal(Story452PinnedTables.EventKinds, result.EventKinds);
        }

        /// <summary>AC4 — WARN+ messages equal the pinned list.</summary>
        [Fact]
        public async Task LogsThePinnedWarnings()
        {
            var result = await RunScriptAsync();

            Assert.Equal(Story452PinnedTables.Warnings, result.Warnings);
        }
    }

    public sealed class ScenarioTheFileHeader
    {
        // Given: this file's own leading comment

        /// <summary>AC5 — the header states the freeze and names gh-#401 and the one PR (T522) that may add an assertion.</summary>
        [Fact]
        public void DeclaresTheFreeze()
        {
            var header = ReadOwnSource();

            Assert.True(header.Contains("FROZEN", StringComparison.Ordinal)
                && header.Contains("gh-#401", StringComparison.Ordinal) && header.Contains("T522", StringComparison.Ordinal));
        }

        static string ReadOwnSource([CallerFilePath] string path = "") => File.ReadAllText(path);
    }

    public sealed class ScenarioThePlanTraces
    {
        // Given: the same run, BreakPlan now exists (T522)

        /// <summary>AC6 — ToTrace() per unit equals the pinned table.</summary>
        [Fact]
        public async Task MatchThePinnedTraceTable()
        {
            var result = await RunScriptAsync();

            Assert.Equal(Story452PinnedTables.Traces, result.Traces);
        }
    }
}
