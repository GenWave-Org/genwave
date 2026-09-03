// STORY-329 — Banter on the air (gh-#385 · SPEC F127.1/.8/.9 · PLAN VQ-i, T281 + T287)
//
// BDD specification — xUnit, pending until /build-loop turns them green. Banter owns its
// moment: a new SegmentKind vending at mid-block seams only (the F92/F124 boundary ladder
// structurally untouched), superseding the F107.5/F116 gated lanes in any break it airs —
// one voice-moment per break, the epic's recorded #1 risk honored. `Crosstalk:Shows`
// empty = OFF, fail-closed: no station's sound changes on upgrade. One assertion per
// Fact; happy first; sad segregated. The T288 wire acceptance (byte-identical air with
// the list emptied, on the production binary) is a production check, not represented
// here. ⛔ T287 carries the Orchestrator drain-region serialization flag.
//
// T287 (this file's own facts, below): all 12 originally-pending facts are now live, plus
// the Nth-airing counter and the failure-path delete this task's own rider required
// (ScenarioTheCadenceKnob's two facts test CrosstalkPlanner.NoteOnAirShow/TryVend directly —
// the SAME "no Orchestrator needed" style Story328_StockedAheadAiredOnce.cs already
// established for CrosstalkPlanner's own decisions; every other fact drives the real
// Orchestrator.GetNextAsync through ProductionChainHarness, widened here with an optional
// CrosstalkPlanner). The gated-lane SUPPRESSION itself (GenWave.Tts.LlmCopyWriter never even
// asking IContextPatterFactSource/IShowFlavorLineSource) is pinned one project over —
// GenWave.Tts.Tests/Specs/Story329_CrosstalkSupersedesTheGatedLanes.cs — this project has no
// ProjectReference to GenWave.Tts, so the facts here stop at proving the ORCHESTRATOR stamped
// SegmentRequest.CrosstalkAiredThisBreak correctly (the Story309_ShowIdentDrain.cs precedent
// for splitting a cross-project proof this same way).

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBanterOnTheAir
{
    // ── Shared fixture (spec-local, mirrors Story309_ShowIdentDrain.cs's own idiom) ────────────

    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
    static readonly DateTimeOffset MidMorning = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    const long HostPersonaId = 10;
    const long NeighborPersonaId = 20;
    const string ShowSlug = "morning-mix";

    static readonly ShowSummary MorningMix =
        new(Id: 5, Name: "Morning Mix", Tagline: null, Flavor: null) { Slug = ShowSlug };

    static readonly CrosstalkCast HostAndNeighbor = new(HostPersonaId, NeighborPersonaId);

    /// <summary>Two all-day blocks: the host's own show (0-720, Monday) with a DISTINCT-persona
    /// neighbor immediately after (720-1440) — grid adjacency casts (Host, Neighbor) by construction
    /// (SPEC F127.2's own "next preferred" rule), and the real schedule boundary between them sits
    /// TWO HOURS ahead of <see cref="MidMorning"/>, well outside every fact's own (short) lookahead
    /// window — the handoff ceremony producer never arms anything competing for these facts.</summary>
    static ScheduleWeekSnapshot TwoBlockGrid() => new(
    [
        new ScheduleSegment(
            Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: HostPersonaId,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningMix, ShowId: MorningMix.Id),
        new ScheduleSegment(
            Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: NeighborPersonaId,
            Genres: null, EnergyMin: null, EnergyMax: null),
    ]);

    static readonly CadenceConfig LeadInOnly = new()
    {
        LeadInBeforeEachTrack = true,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static CrosstalkAiredScript SampleScript() => new(
    [
        new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Did you catch that new single?", false),
        new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "I did — it's on repeat over here.", true),
    ]);

    static StockedCrosstalkExchange MakeReadyExchange(string assetPath, CrosstalkAiredScript? script = null) =>
        new(ShowSlug, HostAndNeighbor, assetPath, new Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 6_000,
            Script: script ?? SampleScript());

    /// <summary>Builds the production chain PLUS a <see cref="CrosstalkPlanner"/> wired into it (or
    /// <see langword="null"/> when a fact needs to prove the feature's Host-wiring-never-ran
    /// no-op).</summary>
    static (ProductionChainHarness.ProductionChain Chain, CrosstalkPlanner Planner, CapturingLogger<CrosstalkPlanner> PlannerLog)
        BuildChain(bool enableShow = true, int everyNthAiring = 1)
    {
        var scope = new FakeCrosstalkScopeProvider(
            enabledShows: enableShow ? [ShowSlug] : [], everyNthAiring: everyNthAiring);
        var plannerLog = new CapturingLogger<CrosstalkPlanner>();
        var planner = new CrosstalkPlanner(new FakePersonaStore(), scope, plannerLog);

        var chain = ProductionChainHarness.BuildProductionChain(
            new FakePersonaStore(), TwoBlockGrid(), MidMorning, TimeSpan.FromMinutes(10),
            cadence: LeadInOnly, catalog: new FakeMediaCatalog(ProductionChainHarness.MakeTrackRef("t1")),
            crosstalkPlanner: planner);

        return (chain, planner, plannerLog);
    }

    /// <summary>Drains exactly <paramref name="itemCount"/> items off the ONE unit LeadInOnly's own
    /// cadence just planned (crosstalk?, lead-in, the track itself) — never more: a 4th pull would
    /// plan a SECOND unit (its own fresh lead-in), which every caller here must not mistake for this
    /// SAME break's own.</summary>
    static async Task<List<MediaItem>> DrainWholeUnitAsync(Orchestrator orchestrator, int itemCount)
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < itemCount; i++)
        {
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(item);
            items.Add(item);
        }

        return items;
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioANewKindAtAMidBlockSeam
    {
        [Fact]
        public static void SegmentKind_Crosstalk_exists_as_an_additive_member()
        {
            // The published Abstractions contract grows by one enum member — minor version, no
            // binary break. One assertion, pinning (name, underlying int) pairs together (round-2
            // T338 review): a name-only sequence proves append-only order and declaration order, but
            // an explicit renumbering that preserved that same order (e.g. inserting a member mid-
            // sequence and shifting every later member's value by one) would still pass a name-only
            // assertion while binary-breaking the published NuGet — every caller that stored an int
            // (a database column, a wire payload) would silently read back the wrong member. Pairing
            // each name with its live `(int)kind` closes that gap. This one assertion now pins all
            // three properties the "additive member" claim needs together: append-only (Crosstalk,
            // then Announcement, land at the END), declaration order (each name sits at the position
            // its source-file declaration puts it in), and every member's underlying value (0..9,
            // unchanged and un-renumbered). T338 (SPEC F144.1, STORY-358) appended Announcement the
            // same way Crosstalk was appended under T281; T390 (SPEC F158.1, STORY-384) appended Ad
            // the same way; this pin now covers all three additions, and a future member extends the
            // sequence, never reorders or renumbers it.
            Assert.Equal(
                [("StationId", 0), ("LeadIn", 1), ("BackAnnounce", 2), ("TimeDate", 3), ("SignOff", 4),
                 ("SignOn", 5), ("ContextSegment", 6), ("Crosstalk", 7), ("Announcement", 8), ("Ad", 9)],
                Enum.GetValues<SegmentKind>().Select(kind => (kind.ToString(), (int)kind)));
        }

        [Fact]
        public static async Task A_due_exchange_vends_at_a_mid_block_break_seam()
        {
            // Given a stocked exchange due per cadence (a single enabled show, one ready exchange)
            var (chain, planner, _) = BuildChain();
            var assetPath = Path.GetTempFileName();
            planner.Stock(MakeReadyExchange(assetPath));

            // When it vends — the FIRST item any ordinary unit ever hands back, ahead of the lead-in
            // and the track itself (Kick/KickResolved call order, SPEC F127.6's "one cached asset the
            // feeder treats as a normal item")
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then it airs as SegmentKind.Crosstalk
            Assert.NotNull(item);
            Assert.Equal(SegmentKind.Crosstalk, item.SegmentKind);
        }

        [Fact]
        public static async Task No_exchange_ever_vends_inside_the_boundary_ceremony_window()
        {
            // Given a stocked, eligible exchange for the on-air show AND a due SignOff that gh-#300's
            // floor forces this unit to decline into a ceremony-only unit (200s already queued ahead
            // of a 45s-out boundary — ArrangeTheIncident's own proven numbers, Gh300_DeclineTheFinalUnit.cs)
            var (chain, planner, _) = BuildChain();
            var assetPath = Path.GetTempFileName();
            planner.Stock(MakeReadyExchange(assetPath));
            chain.Queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: forces the gh-#300 decline",
                chain.Time.GetUtcNow() + TimeSpan.FromSeconds(30),
                new HandoffContext("default", "Host DJ", null));

            // When the unit is planned — the F92/F124 ladder's own ceremony-only shape (next: null)
            var item = await chain.Orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            // Then it airs as the ceremony's own SignOff, never SegmentKind.Crosstalk — excluded
            // because this IS gh-#300's own ceremony-only unit (next is null;
            // TryServeCeremonyOnlyUnitAsync's own EnqueuePatterAsync call passes it), not merely
            // unlucky timing, proven by the exchange still sitting in stock, unvended. This is only
            // ONE of the three exclusions the class's own "Crosstalk" remarks name — see the fact
            // below for the one a due-but-not-declined (e.g. already past-due) ceremony needs instead
            // (SPEC F127.8 review F2's own peek).
            Assert.NotNull(item);
            Assert.NotEqual(SegmentKind.Crosstalk, item.SegmentKind);
            Assert.Equal(1, planner.StockCount(ShowSlug));
        }

        [Fact]
        public static async Task No_exchange_vends_when_a_SignOff_is_already_past_due()
        {
            // Given a stocked, eligible exchange for the on-air show AND a SignOff ALREADY due 1s in
            // the past (SPEC F127.8 review F2 — the reviewer's own live repro). No BoundaryFitPlan is
            // EVER built for it (GetNextAsync's own `untilDue > TimeSpan.Zero` peek-gate excludes
            // anything not strictly future), so neither of the two STRUCTURAL exclusions the class's
            // own "Crosstalk" remarks name (next is null / drainAsOf is not null) ever sees this
            // ceremony — a genuine candidate is picked as usual, an ordinary music unit. Yet it still
            // drains a few lines later in this SAME unit via the ordinary TryDequeueDue(now) call
            // (Due <= now) — pre-fix, AIRED ORDER was [Crosstalk, SignOff, LeadIn].
            var (chain, planner, _) = BuildChain();
            var assetPath = Path.GetTempFileName();
            planner.Stock(MakeReadyExchange(assetPath));
            chain.Queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: already past due",
                chain.Time.GetUtcNow() - TimeSpan.FromSeconds(1),
                new HandoffContext("default", "Host DJ", null));

            // When the unit is planned...
            var items = await DrainWholeUnitAsync(chain.Orchestrator, itemCount: 3); // sign-off, lead-in, track

            // Then the overdue ceremony still airs, in the SAME break the exchange would otherwise
            // have vended into — but no item in that break is SegmentKind.Crosstalk, and the exchange
            // itself stays in stock, unvended, for the NEXT break.
            Assert.DoesNotContain(items, i => i.SegmentKind == SegmentKind.Crosstalk);
            Assert.Equal(1, planner.StockCount(ShowSlug));
        }
    }

    public static class ScenarioBanterSupersedesTheGatedLanes
    {
        [Fact]
        public static async Task No_show_flavor_line_airs_in_a_crosstalk_break()
        {
            // Given a break vending crosstalk
            var (chain, planner, _) = BuildChain();
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            await DrainWholeUnitAsync(chain.Orchestrator, itemCount: 3); // crosstalk, lead-in, track

            // Then the SAME break's lead-in request carries CrosstalkAiredThisBreak — the ONE signal
            // GenWave.Tts.LlmCopyWriter's own show-flavor seam gates on (SPEC F116.3, proven not to be
            // even ASKED in Story329_CrosstalkSupersedesTheGatedLanes.cs, GenWave.Tts.Tests — this
            // project has no ProjectReference to GenWave.Tts to assert on the writer itself).
            var leadIn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.LeadIn);
            Assert.True(leadIn.CrosstalkAiredThisBreak);
        }

        [Fact]
        public static async Task No_context_patter_fact_airs_in_a_crosstalk_break()
        {
            // Given a break vending crosstalk — the SAME evidence as the fact above, since ONE
            // Orchestrator-side signal (SegmentRequest.CrosstalkAiredThisBreak) supersedes BOTH gated
            // lanes at once (SPEC F127.9's own "one voice-moment per break", not two separate gates).
            var (chain, planner, _) = BuildChain();
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            await DrainWholeUnitAsync(chain.Orchestrator, itemCount: 3); // crosstalk, lead-in, track

            var leadIn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.LeadIn);
            Assert.True(leadIn.CrosstalkAiredThisBreak);
        }
    }

    public static class ScenarioTheCadenceKnob
    {
        // ── CrosstalkPlanner-level facts (no Orchestrator needed) — spec-local helpers mirroring
        // Story328_StockedAheadAiredOnce.cs's own Segment/MakeStocked idiom exactly.

        static ScheduleSegment Segment(long? id, DayOfWeek day, int startMinute, int endMinute, long? personaId) =>
            new(Id: id, Day: day, StartMinute: startMinute, EndMinute: endMinute, PersonaId: personaId,
                Genres: null, EnergyMin: null, EnergyMax: null);

        [Fact]
        public static void One_exchange_airs_per_Nth_eligible_airing_of_an_enabled_show()
        {
            // Given Crosstalk:EveryNthAiring set to 2, and one ready exchange for an enabled show
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: HostPersonaId);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: NeighborPersonaId);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var scope = new FakeCrosstalkScopeProvider(enabledShows: [ShowSlug], everyNthAiring: 2);
            var planner = new CrosstalkPlanner(new FakePersonaStore(), scope, NullLogger<CrosstalkPlanner>.Instance);
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            // When the show's FIRST airing begins (count=1, 1 % 2 != 0 — not yet due)...
            planner.NoteOnAirShow(ShowSlug);

            // Then that airing vends nothing, however many mid-block seams ask
            Assert.Null(planner.TryVend(ShowSlug, host, snapshot));
            Assert.Null(planner.TryVend(ShowSlug, host, snapshot));

            // When the show leaves the air and returns — its SECOND airing (count=2, 2 % 2 == 0)...
            planner.NoteOnAirShow(null);
            planner.NoteOnAirShow(ShowSlug);

            // Then exactly that airing vends the one exchange still sitting in stock
            Assert.NotNull(planner.TryVend(ShowSlug, host, snapshot));
        }

        [Fact]
        public static void The_cadence_setting_is_live_editable_with_a_default_of_one()
        {
            // Given the shipped default (EveryNthAiring omitted from FakeCrosstalkScopeProvider = 1)
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: HostPersonaId);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: NeighborPersonaId);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var scope = new FakeCrosstalkScopeProvider(enabledShows: [ShowSlug]);
            var planner = new CrosstalkPlanner(new FakePersonaStore(), scope, NullLogger<CrosstalkPlanner>.Instance);
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            // When the show's very first airing begins — default 1 airs every single time
            planner.NoteOnAirShow(ShowSlug);
            Assert.NotNull(planner.TryVend(ShowSlug, host, snapshot));

            // When an operator's live PUT raises the knob to 3, and a fresh exchange stocks for the
            // NEXT airing...
            scope.EveryNthAiring = 3;
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));
            planner.NoteOnAirShow(null);
            planner.NoteOnAirShow(ShowSlug); // airing #2 — 2 % 3 != 0

            // Then that next airing does NOT vend — the live edit took effect with no api restart
            Assert.Null(planner.TryVend(ShowSlug, host, snapshot));
        }

        [Fact]
        public static void At_most_one_exchange_vends_per_airing_even_with_a_second_still_in_stock()
        {
            // Given TWO ready, fresh (matching current adjacency) exchanges for an enabled show — SPEC
            // F127.7's own "airs once" ruling is about the OCCURRENCE, not merely stock exhaustion:
            // this fact is what a mutant dropping the vendedThisAiring/alreadyVended check from
            // TryVend's eligibility gate (SPEC F127.8 review F6's own fold) would survive, since
            // A_retired_exchange_can_never_vend_again's own single-exchange stock empties on its own
            // regardless of that flag.
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: HostPersonaId);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: NeighborPersonaId);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var scope = new FakeCrosstalkScopeProvider(enabledShows: [ShowSlug]);
            var planner = new CrosstalkPlanner(new FakePersonaStore(), scope, NullLogger<CrosstalkPlanner>.Instance);
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));
            planner.NoteOnAirShow(ShowSlug);

            // When this airing vends once...
            Assert.NotNull(planner.TryVend(ShowSlug, host, snapshot));

            // Then a SECOND vend attempt for the SAME airing hands out nothing — even though a second,
            // equally fresh exchange still sits in stock, ready for the NEXT airing instead.
            Assert.Null(planner.TryVend(ShowSlug, host, snapshot));
            Assert.Equal(1, planner.StockCount(ShowSlug));
        }
    }

    public static class ScenarioTheAiredScriptIsOnTheRecord
    {
        [Fact]
        public static async Task The_booth_row_carries_the_full_script_in_its_stamp()
        {
            // Given a stocked exchange carrying a full two-voice script
            var (chain, planner, _) = BuildChain();
            var script = SampleScript();
            planner.Stock(MakeReadyExchange(Path.GetTempFileName(), script));

            // When it vends...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the composed item carries the SAME script forward, unchanged — the value
            // PlayoutFeeder forwards onto TrackAired.CrosstalkScript, and BoothLogWriter (GenWave.MediaLibrary,
            // no ProjectReference from this project) serializes into station.booth_log.pick (SPEC F127.11 —
            // proven at that layer in GenWave.MediaLibrary.Tests/Specs/Story329_CrosstalkBoothStamp.cs).
            Assert.NotNull(item);
            Assert.Equal(SegmentKind.Crosstalk, item.SegmentKind);
            Assert.Same(script, item.CrosstalkScript);
        }

        [Fact]
        public static async Task The_demo_hour_instrument_counts_a_Crosstalk_row_like_any_kind()
        {
            // Given a stocked, ready exchange
            var (chain, planner, _) = BuildChain();
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            // When it vends...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the item carries the SAME SegmentKind stamp every other aired kind already does —
            // tools/demo_hour_gate.sql's own has_other_non_music_kind predicate (segment_kind IS NOT
            // NULL AND NOT IN ('StationId','ContextSegment')) counts it with zero code changes (F127.12
            // — verified, not built); PlayoutFeeder/BoothLogWriter forward it unchanged, exactly like
            // every pre-F127 kind.
            Assert.NotNull(item);
            Assert.Equal(SegmentKind.Crosstalk, item.SegmentKind);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    // Named "MeansOff", not "...ByteIdentical" (round-2 review F11's tail — an honesty fix over the
    // over-claiming original name): this project has no ProjectReference to GenWave.Tts, so nothing
    // below can compare a single byte. The actual byte-identical proof (a station with Crosstalk:Shows
    // empty renders EXACTLY the pre-F127 prompt) lives in the F107/F116 golden byte-pins over in
    // GenWave.Tts.Tests. What this scenario proves is narrower but load-bearing for that proof to
    // hold: the ONE Orchestrator-side trigger those goldens' whole byte-identity claim depends on
    // (SegmentRequest.CrosstalkAiredThisBreak) never fires with the feature off — this class delegates
    // the byte-identity claim itself to the goldens, it does not re-prove it.
    public static class ScenarioAnEmptyListMeansOff
    {
        [Fact]
        public static async Task With_Crosstalk_Shows_empty_no_exchange_ever_vends()
        {
            // Given the shipped default (Crosstalk:Shows empty) — even with a ready, eligible-looking
            // exchange already stocked (proving the fail-closed gate, never merely "nothing to vend")
            var (chain, planner, _) = BuildChain(enableShow: false);
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            // When any break airs...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then no crosstalk machinery affects it — the lead-in airs, never a Crosstalk row, and the
            // exchange is never removed from stock
            Assert.NotNull(item);
            Assert.NotEqual(SegmentKind.Crosstalk, item.SegmentKind);
            Assert.Equal(1, planner.StockCount(ShowSlug));
        }

        [Fact]
        public static async Task With_Crosstalk_Shows_empty_the_gated_lane_arbitration_is_unchanged()
        {
            // Given the shipped default (Crosstalk:Shows empty), a ready exchange notwithstanding
            var (chain, planner, _) = BuildChain(enableShow: false);
            planner.Stock(MakeReadyExchange(Path.GetTempFileName()));

            // When the break's lead-in plans...
            await DrainWholeUnitAsync(chain.Orchestrator, itemCount: 2); // lead-in, track — no crosstalk

            // Then CrosstalkAiredThisBreak never flips true — the F107/F116 golden byte-pins
            // (GenWave.Tts.Tests) hold because THIS Orchestrator-side signal, their whole trigger,
            // never fires with the feature off.
            var leadIn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.LeadIn);
            Assert.False(leadIn.CrosstalkAiredThisBreak);
        }
    }

    public static class ScenarioAnEmptyStockSkipsSilently
    {
        [Fact]
        public static async Task A_due_airing_with_no_ready_exchange_skips_the_slot()
        {
            // Given an enabled show with an EMPTY stock (ramp-up — nothing has generated yet)
            var (chain, _, _) = BuildChain();

            // When the vend runs...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the slot skips — no SegmentKind.Crosstalk item ever appears
            Assert.NotNull(item);
            Assert.NotEqual(SegmentKind.Crosstalk, item.SegmentKind);
        }

        [Fact]
        public static async Task The_skipped_break_proceeds_with_its_ordinary_lanes()
        {
            // Given an enabled show with an EMPTY stock
            var (chain, _, _) = BuildChain();

            // When the break plans...
            var items = await DrainWholeUnitAsync(chain.Orchestrator, itemCount: 2); // lead-in, track

            // Then the break proceeds exactly as if crosstalk never existed — the lead-in and the
            // track both still air, neither flavored by a break that never happened
            Assert.Contains(items, i => i.SegmentKind == SegmentKind.LeadIn);
            Assert.Contains(items, i => i.SegmentKind is null); // the music track itself
            var leadIn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.LeadIn);
            Assert.False(leadIn.CrosstalkAiredThisBreak);
        }
    }

    // ── The failure-path delete (T287's own rider — not in the original 12) ──────────────────

    public static class ScenarioTheFailurePathNeverLeaksTheAsset
    {
        [Fact]
        public static async Task A_vended_exchange_whose_asset_vanished_never_airs_and_is_discarded()
        {
            // Given a stocked, otherwise-eligible exchange whose asset is missing — TryVend has
            // already removed it from stock by the time this integration ever observes the vanished
            // file (a race with a fresh CrosstalkStockWorker's own startup purge, or a file deleted
            // out of band)
            var (chain, planner, plannerLog) = BuildChain();
            var neverWrittenPath = Path.Combine(Path.GetTempPath(), $"crosstalk-missing-{Guid.NewGuid():N}.wav");
            planner.Stock(MakeReadyExchange(neverWrittenPath));

            // When the break plans...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then nothing airs as crosstalk (the break falls back to its ordinary lanes, exactly like
            // an empty stock) — the exchange is gone from stock (single-use, already spent) — and the
            // discard is on the record, distinctly worded from a genuine "retired after airing" line
            Assert.NotNull(item);
            Assert.NotEqual(SegmentKind.Crosstalk, item.SegmentKind);
            Assert.Equal(0, planner.StockCount(ShowSlug));
            Assert.Contains(
                plannerLog.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("never reached air", StringComparison.OrdinalIgnoreCase));
        }
    }
}
