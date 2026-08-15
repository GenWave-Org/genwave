// STORY-328 — Stocked ahead, aired once (gh-#385 · SPEC F127.2/.7 · PLAN VQ-i, T285–T286)
//
// BDD specification — xUnit, pending until /build-loop turns them green. The single-use
// queue ruling: exchanges generate and render OFF the on-air clock (LLM latency stops
// mattering), air once, retire at air — re-airing is the "said that before" artifact the
// 07-31 evergreen rejection named. Casting comes free from the grid (drop-in neighbor:
// no authoring surface, no show→persona reference). No schema: a restart regenerates
// (the F125.4 durability posture). One assertion per Fact; happy first; sad segregated.
// The T288 wire acceptance is a production check, not represented here.
// ⛔ Gated behind T283's paper-audition go.
//
// T285 (this file's own facts, below): CrosstalkPlanner's casting/retire/staleness/restart
// behavior, plus the SPEC F127.8 eligibility gate (the "empty Shows = OFF" fail-closed rule) —
// added beyond the original scaffold's 9 Pending-T285 facts because the mutation self-run this
// task requires (the empty-Shows-means-OFF pin) needs a live fact to kill.
//
// T286 (this file's ScenarioTheStockFillsOffTheClock/ScenarioNeverInsideABreakWindow groups): the
// two Pending-T286 facts are now live, pinning CrosstalkPlanner.NeedsStock (the "is this show below
// target" decision) and CrosstalkBreakWindow.IsOpen (the "never inside a break window" gate) — the
// two framework-free deciders CrosstalkStockWorker's Host timer shell (PLAN T286) consults every
// tick. Siblings beyond the original 2 pin the mutation surface each decider's own comparison
// actually needs (an off-by-one on '<'/'<=', an inverted null-check) — see each fact's own remarks.
//
// PLAN T285 review round 2 (F1/F2/F3/F6/F9, design note): adjacency is now CYCLIC (F1) — the
// wraparound fact below is what a linear revert reds; "previous casts when no next" is re-pinned
// to a music-only NEXT rather than an absent one (closes half of F3, and is what a
// gate-on-`next is null` mutant reds); "no adjacent persona at all" is re-pinned to neighbors that
// EXIST but carry none (AC5's real sad path), with a new sibling fact for a music-only HOST (the
// other half of F3); TryVend now fail-closes on scope itself (F2) and treats an unresolvable host
// as uncertainty, never staleness (F6); Stock() defends its own StockTargetPerShow invariant
// (design note). Every "showName" fixture below is a SLUG (`morning-drive`), matching PLAN T285
// review F4's production shape.

namespace GenWave.Orchestration.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureStockedAheadAiredOnce
{
    // ── Shared fixtures ─────────────────────────────────────────────────────

    static ScheduleSegment Segment(long? id, DayOfWeek day, int startMinute, int endMinute, long? personaId) =>
        new(Id: id, Day: day, StartMinute: startMinute, EndMinute: endMinute, PersonaId: personaId,
            Genres: null, EnergyMin: null, EnergyMax: null);

    static PersonaCard MakeCard(string name) =>
        new(1, name, "", "", [], new VoiceSpec("kokoro", "", 1.0, "en"), EnergyDisposition: 0.0, [], []);

    static StockedCrosstalkExchange MakeStocked(string showSlug, CrosstalkCast cast, string assetPath) =>
        new(showSlug, cast, assetPath, new Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 5000);

    static CrosstalkPlanner MakePlanner(FakePersonaStore personaStore, FakeCrosstalkScopeProvider? scope = null) =>
        new(personaStore, scope ?? new FakeCrosstalkScopeProvider(), NullLogger<CrosstalkPlanner>.Instance);

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioCastingComesFreeFromTheGrid
    {
        [Fact]
        public static async Task The_second_voice_is_the_next_blocks_persona_when_one_exists()
        {
            // Given an enabled show's host block with a DISTINCT persona on both sides — next AND
            // previous — so preference, not mere availability, is what this fact proves.
            var previous = Segment(1, DayOfWeek.Monday, 0, 480, personaId: 30);
            var host = Segment(2, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(3, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([previous, host, next]);

            var personaStore = new FakePersonaStore();
            personaStore.AddCard(10, MakeCard("Host DJ"));
            personaStore.AddCard(20, MakeCard("Next DJ"));
            personaStore.AddCard(30, MakeCard("Previous DJ"));
            var planner = MakePlanner(personaStore);

            var result = await planner.TryCastAsync(host, snapshot, CancellationToken.None);

            // Then host = the show's DJ, second = the NEXT block's persona (tease-forward wins)
            Assert.Equal(new CrosstalkCast(HostPersonaId: 10, NeighborPersonaId: 20), result?.Cast);
        }

        [Fact]
        public static async Task The_previous_blocks_persona_casts_when_the_next_block_is_music_only()
        {
            // Given the next block EXISTS but carries no persona (music-only) — under cyclic
            // adjacency (PLAN T285 review F1) a host always HAS a next, so the real "no next"
            // fallback trigger is disqualification, not absence (SPEC F91.1's own grid-repeats
            // semantics — see CrosstalkPlanner's own remarks); this is what a mutant gating the
            // fallback on "next is null" (rather than "next doesn't qualify") reds.
            var previous = Segment(1, DayOfWeek.Monday, 0, 480, personaId: 30);
            var host = Segment(2, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(3, DayOfWeek.Monday, 960, 1440, personaId: null);
            var snapshot = new ScheduleWeekSnapshot([previous, host, next]);

            var personaStore = new FakePersonaStore();
            personaStore.AddCard(10, MakeCard("Host DJ"));
            personaStore.AddCard(30, MakeCard("Previous DJ"));
            var planner = MakePlanner(personaStore);

            var result = await planner.TryCastAsync(host, snapshot, CancellationToken.None);

            Assert.Equal(new CrosstalkCast(HostPersonaId: 10, NeighborPersonaId: 30), result?.Cast);
        }

        [Fact]
        public static void The_last_blocks_next_is_the_first_block()
        {
            // Given a host block that is the LAST in the week's own chronological order (Saturday
            // late), a distinct first-of-week block (Sunday early — the exact showcase handoff SPEC
            // F127.2 review F1 names), and a distinct filler block whose only job is to prove
            // PREVIOUS was not merely reached by coincidence
            var first = Segment(1, DayOfWeek.Sunday, 0, 480, personaId: 99);
            var middle = Segment(2, DayOfWeek.Monday, 0, 480, personaId: 77);
            var host = Segment(3, DayOfWeek.Saturday, 1320, 1440, personaId: 10);
            var snapshot = new ScheduleWeekSnapshot([first, middle, host]);

            var cast = CrosstalkPlanner.TryCastPersonas(host, snapshot);

            // Then the cast neighbor is the FIRST block's persona — the grid wraps (SPEC F91.1,
            // matching ScheduleResolver.CyclicDistance's own semantics one file over) — never the
            // middle filler block a LINEAR "no next, fall back to previous" reading would produce.
            Assert.Equal(new CrosstalkCast(HostPersonaId: 10, NeighborPersonaId: 99), cast);
        }
    }

    public static class ScenarioTheStockFillsOffTheClock
    {
        [Fact]
        public static void A_show_below_its_stock_target_triggers_generation()
        {
            // Given a show holding no stock at all — below StockTargetPerShow's floor of 2 (SPEC
            // F127.7)
            var planner = MakePlanner(new FakePersonaStore());

            var needsStock = planner.NeedsStock("morning-drive");

            // Then the stock-timer loop's own trigger reads true — generation is worth attempting
            Assert.True(needsStock);
        }

        [Fact]
        public static void A_show_already_at_its_stock_target_does_not_trigger_generation()
        {
            // Given a show already holding StockTargetPerShow (2) ready exchanges — the sibling of
            // the fact above, closing the '<' vs '<=' mutant NeedsStock's own comparison could hide
            var planner = MakePlanner(new FakePersonaStore());
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 20), Path.GetTempFileName()));
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 21), Path.GetTempFileName()));

            var needsStock = planner.NeedsStock("morning-drive");

            // Then the trigger reads false — nothing left to fill
            Assert.False(needsStock);
        }
    }

    public static class ScenarioNeverInsideABreakWindow
    {
        // RELOCATION NOTE (PLAN T286 review F2, honesty requirement): the original scaffold's
        // `The_worker_never_generates_or_renders_inside_a_break_window` (Pending T286) named the
        // WORKER's own end-to-end behavior — that CrosstalkStockWorker itself, not merely
        // CrosstalkBreakWindow.IsOpen in isolation, never issues a script-writer/assembler call
        // while a break window is open. Turning that placeholder live here would only ever re-prove
        // the pure decider below, not the wiring between it and TickOnceAsync — so the facts below
        // are the pure, framework-free half (no Host, no HTTP, no ollama) and the worker-behavior
        // half now lives in GenWave.Host.Tests (Story328_CrosstalkStockWorker.cs,
        // ScenarioTheWorkerNeverGeneratesInsideABreakWindow), which can actually drive the real
        // CrosstalkScriptWriter/CrosstalkAssembler and assert zero outbound calls.

        // A render-budget/started-at pairing that sits comfortably clear of the after-transition
        // face (check 2) for every fact below that means to isolate the end-of-item margin face
        // (check 3) instead — mirrors CrosstalkBreakWindow's own three-check remarks.
        static readonly TimeSpan RenderBudget = TimeSpan.FromSeconds(30);
        static readonly DateTimeOffset LongAgo =
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero) - TimeSpan.FromHours(1);

        [Fact]
        public static void A_break_window_reads_open_within_the_safety_margin()
        {
            // Given the current on-air item's estimated end sits just inside CrosstalkBreakWindow's
            // own safety margin — imminent, not yet arrived (isolating check 3: the item started
            // long ago, clear of check 2's after-transition window, and nothing is mid-render)
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var estimatedEndsAt = now + CrosstalkBreakWindow.Margin - TimeSpan.FromSeconds(1);

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, LongAgo, RenderBudget, refillInFlight: false);

            // Then the window reads open — the stock-timer must not start (or must abandon) work
            Assert.True(isOpen);
        }

        [Fact]
        public static void A_break_window_reads_open_exactly_at_the_safety_margin()
        {
            // Given the current on-air item's estimated end sits EXACTLY Margin away — the inclusive
            // boundary (closes the '<=' vs '<' mutant the two comfortably-inside/outside facts above
            // cannot: both sit strictly clear of the boundary by design, to avoid a flaky clock-math
            // off-by-one)
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var estimatedEndsAt = now + CrosstalkBreakWindow.Margin;

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, LongAgo, RenderBudget, refillInFlight: false);

            // Then the window still reads open — the margin itself is the last safe instant to start
            Assert.True(isOpen);
        }

        [Fact]
        public static void A_break_window_reads_closed_comfortably_before_the_margin()
        {
            // Given the current on-air item's estimated end sits well past the safety margin, AND
            // it is comfortably clear of the after-transition window and no render is in flight
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var estimatedEndsAt = now + CrosstalkBreakWindow.Margin + TimeSpan.FromMinutes(5);

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, LongAgo, RenderBudget, refillInFlight: false);

            // Then the window reads closed — off the on-air clock, generation may proceed. Paired
            // with the fact above (an open/closed discrimination pair) so neither reads true/false
            // vacuously regardless of the estimated end.
            Assert.False(isOpen);
        }

        [Fact]
        public static void An_unknown_estimated_end_reads_the_break_window_as_open()
        {
            // Given no estimated end at all (an engine-initiated/foreign on-air item, or no tick has
            // published a snapshot yet) — SPEC F127.7's "never inside a break window" is a hard
            // constraint, so an unknown state must never be read as permission to generate
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var isOpen = CrosstalkBreakWindow.IsOpen(
                now, estimatedOnAirEndsAt: null, LongAgo, RenderBudget, refillInFlight: false);

            // Then the window reads open — fail-closed
            Assert.True(isOpen);
        }

        // ── PLAN T286 review F1: the after-transition and refill-in-flight faces ───────────────

        [Fact]
        public static void A_break_window_reads_open_right_after_a_transition_even_with_a_distant_estimated_end()
        {
            // Given the on-air item just started (gh-#184: the fresh snapshot publishes, THEN
            // RefillAsync fires — the render's hazard begins at the transition, not near the item's
            // end) while its estimated end sits comfortably far away — the shape an end-only fence
            // would wrongly read as closed (this is the F1 finding's own "fence guards the wrong 45
            // seconds")
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var onAirStartedAt = now - TimeSpan.FromSeconds(10);
            var estimatedEndsAt = now + TimeSpan.FromMinutes(10);

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, onAirStartedAt, RenderBudget, refillInFlight: false);

            // Then the window reads open — the after-transition face blocks on its own
            Assert.True(isOpen);
        }

        [Fact]
        public static void An_unknown_on_air_start_reads_the_break_window_as_open()
        {
            // Given no on-air start at all (no snapshot published yet) — the after-transition face's
            // own fail-closed twin to the "unknown estimated end" fact above
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var isOpen = CrosstalkBreakWindow.IsOpen(
                now, estimatedOnAirEndsAt: null, onAirStartedAt: null, RenderBudget, refillInFlight: false);

            Assert.True(isOpen);
        }

        [Fact]
        public static void A_refill_in_flight_blocks_even_mid_item()
        {
            // Given every OTHER face would read closed (comfortably clear of both the after-
            // transition window and the end-of-item margin) — the real signal alone still blocks
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var onAirStartedAt = LongAgo;
            var estimatedEndsAt = now + TimeSpan.FromMinutes(10);

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, onAirStartedAt, RenderBudget, refillInFlight: true);

            Assert.True(isOpen);
        }

        [Fact]
        public static void A_mid_item_quiet_stretch_permits_generation()
        {
            // Given the on-air item started long ago (clear of the after-transition window), its
            // estimated end is far away (clear of the end-of-item margin), and no render is in
            // flight — the positive control proving the two facts above are real gates, not a wire
            // that always reads open regardless of timing/signal
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var onAirStartedAt = LongAgo;
            var estimatedEndsAt = now + TimeSpan.FromMinutes(10);

            var isOpen = CrosstalkBreakWindow.IsOpen(now, estimatedEndsAt, onAirStartedAt, RenderBudget, refillInFlight: false);

            // Then the window reads closed — generation may proceed
            Assert.False(isOpen);
        }
    }

    public static class ScenarioStockDefendsItsOwnTarget
    {
        [Fact]
        public static void A_show_already_at_its_stock_target_refuses_another_exchange()
        {
            // Given a show already holding StockTargetPerShow (2) ready exchanges (PLAN T285 review
            // design note — Stock() enforces its own named invariant rather than trusting every
            // caller to have checked StockCount first)
            var planner = MakePlanner(new FakePersonaStore());
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 20), Path.GetTempFileName()));
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 21), Path.GetTempFileName()));

            // When a third exchange is offered
            var accepted = planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 22), Path.GetTempFileName()));

            // Then it is refused
            Assert.False(accepted);
        }

        [Fact]
        public static void A_show_already_at_its_stock_target_stays_at_the_target_after_a_refusal()
        {
            // Given a show already holding StockTargetPerShow (2) ready exchanges (PLAN T285 review
            // design note — Stock() enforces its own named invariant rather than trusting every
            // caller to have checked StockCount first)
            var planner = MakePlanner(new FakePersonaStore());
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 20), Path.GetTempFileName()));
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 21), Path.GetTempFileName()));

            // When a third exchange is offered
            planner.Stock(MakeStocked("morning-drive", new CrosstalkCast(10, 22), Path.GetTempFileName()));

            // Then the stock stays at the target — the refused exchange was never added
            Assert.Equal(CrosstalkPlanner.StockTargetPerShow, planner.StockCount("morning-drive"));
        }
    }

    public static class ScenarioAiredOnceRetiredAtAir
    {
        [Fact]
        public static void Retirement_deletes_the_aired_exchanges_asset()
        {
            // Given an aired exchange's asset sitting on disk
            var assetPath = Path.GetTempFileName();
            var exchange = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var planner = MakePlanner(new FakePersonaStore());

            planner.Retire(exchange);

            // Then its asset is deleted
            Assert.False(File.Exists(assetPath));
        }

        [Fact]
        public static void A_retired_exchange_can_never_vend_again()
        {
            // Given an exchange that has vended once (removed from stock) and then retired
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var assetPath = Path.GetTempFileName();
            var exchange = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(exchange);
            var vended = planner.TryVend("morning-drive", host, snapshot)
                ?? throw new InvalidOperationException("test setup: expected a fresh exchange to vend");
            planner.Retire(vended);

            // When the same show is vended again
            var vendedAgain = planner.TryVend("morning-drive", host, snapshot);

            // Then nothing is left to vend — it can never air a second time
            Assert.Null(vendedAgain);
        }
    }

    public static class ScenarioAScheduleEditInvalidatesTheCast
    {
        [Fact]
        public static void A_stale_cast_pair_is_discarded_at_vend_with_one_reason_line()
        {
            // Given a stocked exchange whose cast pair no longer matches grid adjacency (current
            // adjacency casts neighbor=20; the stocked exchange still names neighbor=99)
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var assetPath = Path.GetTempFileName();
            var stale = MakeStocked("morning-drive", new CrosstalkCast(10, 99), assetPath);
            var logger = new CapturingLogger<CrosstalkPlanner>();
            var planner = new CrosstalkPlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]), logger);
            planner.Stock(stale);

            planner.TryVend("morning-drive", host, snapshot);

            // Then exactly one Information line names the discard
            Assert.Single(
                logger.Entries,
                e => e.Level == LogLevel.Information && e.Message.Contains("discarded", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static void A_discarded_stale_exchange_is_restocked()
        {
            // Given a stocked exchange whose cast pair no longer matches grid adjacency
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var assetPath = Path.GetTempFileName();
            var stale = MakeStocked("morning-drive", new CrosstalkCast(10, 99), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(stale);

            planner.TryVend("morning-drive", host, snapshot);

            // Then the show's stock is freed — the slot a LATER task's stock-timer loop will refill
            Assert.Equal(0, planner.StockCount("morning-drive"));
        }
    }

    public static class ScenarioAnUnknownHostIsUncertaintyNotStaleness
    {
        [Fact]
        public static void An_unresolvable_host_segment_vends_nothing()
        {
            // Given a stocked exchange, and a "current host block" that is NOT part of the current
            // snapshot at all (PLAN T285 review F6 — e.g. the on-air block is currently a projected
            // special, ScheduleResolver.ProjectSpecial's own negated-id segment, never a member of
            // the WEEKLY ScheduleWeekSnapshot a caller resolves separately)
            var weeklyHost = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([weeklyHost, next]);
            var unresolvableHost = Segment(-1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var assetPath = Path.GetTempFileName();
            var stocked = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(stocked);

            // When vend is attempted against the unresolvable host
            var vended = planner.TryVend("morning-drive", unresolvableHost, snapshot);

            // Then nothing vends
            Assert.Null(vended);
        }

        [Fact]
        public static void An_unresolvable_host_segment_leaves_the_stock_untouched()
        {
            // Given a stocked exchange, and a "current host block" that is NOT part of the current
            // snapshot at all (PLAN T285 review F6 — e.g. the on-air block is currently a projected
            // special, ScheduleResolver.ProjectSpecial's own negated-id segment, never a member of
            // the WEEKLY ScheduleWeekSnapshot a caller resolves separately)
            var weeklyHost = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([weeklyHost, next]);
            var unresolvableHost = Segment(-1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var assetPath = Path.GetTempFileName();
            var stocked = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(stocked);

            // When vend is attempted against the unresolvable host
            planner.TryVend("morning-drive", unresolvableHost, snapshot);

            // Then — unlike a genuine staleness mismatch — the stock is untouched
            Assert.Equal(1, planner.StockCount("morning-drive"));
        }

        [Fact]
        public static void A_null_Id_host_segment_vends_nothing()
        {
            // Given a stocked exchange whose cast happens to equal what a null-Id match bug would
            // spuriously derive, and a "current host block" that carries NO id at all — the OTHER
            // shape SPEC F127.8 review F4/F2's fix guards (an unpersisted
            // ScheduleResolver.ProjectSpecial that has never had a negated id assigned) — plus a
            // grid whose own only segment ALSO carries no id (a decoy this exact host id could
            // null-match if the guard were ever reverted)
            var decoy = Segment(id: null, DayOfWeek.Monday, 480, 960, personaId: 99);
            var snapshot = new ScheduleWeekSnapshot([decoy]);
            var nullIdHost = Segment(id: null, DayOfWeek.Monday, 480, 960, personaId: 10);
            var assetPath = Path.GetTempFileName();
            var stocked = MakeStocked("morning-drive", new CrosstalkCast(10, 99), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(stocked);

            // When vend is attempted against the null-Id host
            var vended = planner.TryVend("morning-drive", nullIdHost, snapshot);

            // Then nothing vends — a null host id is never treated as "found" merely because some
            // OTHER grid segment also carries a null id
            Assert.Null(vended);
        }

        [Fact]
        public static void A_null_Id_host_segment_leaves_the_stock_untouched()
        {
            // Given the same null-Id host/decoy shape as the fact above
            var decoy = Segment(id: null, DayOfWeek.Monday, 480, 960, personaId: 99);
            var snapshot = new ScheduleWeekSnapshot([decoy]);
            var nullIdHost = Segment(id: null, DayOfWeek.Monday, 480, 960, personaId: 10);
            var assetPath = Path.GetTempFileName();
            var stocked = MakeStocked("morning-drive", new CrosstalkCast(10, 99), assetPath);
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));
            planner.Stock(stocked);

            // When vend is attempted against the null-Id host
            planner.TryVend("morning-drive", nullIdHost, snapshot);

            // Then the stock is untouched — uncertainty is not evidence the schedule moved
            Assert.Equal(1, planner.StockCount("morning-drive"));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioNoDistinctNeighborNoExchange
    {
        [Fact]
        public static void Adjacent_blocks_sharing_the_host_persona_skip_the_airing()
        {
            // Given both the next AND the previous block share the host's own persona
            var previous = Segment(1, DayOfWeek.Monday, 0, 480, personaId: 10);
            var host = Segment(2, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(3, DayOfWeek.Monday, 960, 1440, personaId: 10);
            var snapshot = new ScheduleWeekSnapshot([previous, host, next]);

            var cast = CrosstalkPlanner.TryCastPersonas(host, snapshot);

            // The host never banters with themself.
            Assert.Null(cast);
        }

        [Fact]
        public static void No_adjacent_persona_at_all_skips_the_airing()
        {
            // Given BOTH neighbors exist but carry no persona at all (music-only blocks either side
            // — SPEC F127.2's AC5, PLAN T285 review F3: genuinely distinct from the same-persona sad
            // path above, and no longer conflatable with a single-segment grid now that adjacency
            // wraps (that grid's own next/previous is itself — the SAME-persona case, not this one).
            var previous = Segment(1, DayOfWeek.Monday, 0, 480, personaId: null);
            var host = Segment(2, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(3, DayOfWeek.Monday, 960, 1440, personaId: null);
            var snapshot = new ScheduleWeekSnapshot([previous, host, next]);

            var cast = CrosstalkPlanner.TryCastPersonas(host, snapshot);

            Assert.Null(cast);
        }

        [Fact]
        public static void A_music_only_host_block_casts_nothing()
        {
            // Given the "host" block itself carries no persona (PLAN T285 review F3 — closes the
            // hostBlock.PersonaId ?? 0 mutant: there is no host voice for a neighbor to react to)
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: null);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([host, next]);

            var cast = CrosstalkPlanner.TryCastPersonas(host, snapshot);

            Assert.Null(cast);
        }

        [Fact]
        public static void A_null_Id_host_segment_casts_nothing()
        {
            // Given a host block that carries NO id at all (SPEC F127.8 review F4/F2's fix guards —
            // an unpersisted ScheduleResolver.ProjectSpecial that has never had a negated id
            // assigned), never itself added to the grid, and a grid whose own only segment ALSO
            // carries no id (a decoy this exact host id could null-match if the guard were ever
            // reverted, wrongly borrowing the decoy's own neighbor as this host's cast)
            var decoy = Segment(id: null, DayOfWeek.Monday, 480, 960, personaId: 99);
            var snapshot = new ScheduleWeekSnapshot([decoy]);
            var nullIdHost = Segment(id: null, DayOfWeek.Monday, 0, 480, personaId: 10);

            var cast = CrosstalkPlanner.TryCastPersonas(nullIdHost, snapshot);

            // Then no cast — a null host id can never be located in the grid, not even by
            // accidentally matching a DIFFERENT null-Id segment
            Assert.Null(cast);
        }
    }

    public static class ScenarioARestartForgetsAndThatIsFine
    {
        [Fact]
        public static void No_stock_state_survives_a_restart()
        {
            // No persisted queue exists — the stock regenerates from nothing, and
            // retirement-by-deletion means nothing ever airs twice.
            var assetPath = Path.GetTempFileName();
            var exchange = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var beforeRestart = MakePlanner(new FakePersonaStore());
            beforeRestart.Stock(exchange);

            // When a fresh process comes back up (a new CrosstalkPlanner instance — no schema)
            var afterRestart = MakePlanner(new FakePersonaStore());

            Assert.Equal(0, afterRestart.StockCount("morning-drive"));
        }
    }

    // ── CONFIG — the SPEC F127.8 eligibility gate ──────────────────────────
    //
    // Not in the original 9-fact scaffold; added for this task's own required mutation self-run
    // (the empty-Shows-means-OFF pin needs a live fact to kill).

    public static class ScenarioTheShowsListIsTheKillSwitch
    {
        [Fact]
        public static void An_empty_Shows_list_disables_every_show()
        {
            // Given no Crosstalk:Shows configured at all (the fail-closed default)
            var planner = MakePlanner(new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: []));

            Assert.False(planner.IsShowEnabled("morning-drive"));
        }

        [Fact]
        public static void A_named_show_is_enabled_only_when_listed()
        {
            // Given Crosstalk:Shows names exactly one show
            var planner = MakePlanner(
                new FakePersonaStore(), new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]));

            Assert.True(planner.IsShowEnabled("morning-drive"));
        }

        [Fact]
        public static void A_stocked_exchange_does_not_vend_after_the_show_list_empties()
        {
            // Given a show that was enabled when its exchange was stocked (PLAN T285 review F2 — the
            // vend face is scope-aware, not merely the eligibility face a caller could forget to
            // re-check)
            var host = Segment(1, DayOfWeek.Monday, 480, 960, personaId: 10);
            var next = Segment(2, DayOfWeek.Monday, 960, 1440, personaId: 20);
            var snapshot = new ScheduleWeekSnapshot([host, next]);
            var assetPath = Path.GetTempFileName();
            var exchange = MakeStocked("morning-drive", new CrosstalkCast(10, 20), assetPath);
            var scope = new FakeCrosstalkScopeProvider(enabledShows: ["morning-drive"]);
            var planner = MakePlanner(new FakePersonaStore(), scope);
            planner.Stock(exchange);

            // When the show list empties (an operator's live PUT) before this exchange ever airs
            scope.EnabledShows = [];
            var vended = planner.TryVend("morning-drive", host, snapshot);

            // Then it does not vend — a stocked exchange does not outlive the show's own eligibility
            Assert.Null(vended);
        }
    }
}
