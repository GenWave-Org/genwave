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
// task requires (the empty-Shows-means-OFF pin) needs a live fact to kill. The 2 Pending-T286
// facts (stock-fill trigger, break-window fence) stay skipped — the Host stock-timer loop is a
// LATER task.
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
        [Fact(Skip = "Pending T286 — see docs/PLAN.md")]
        public static void A_show_below_its_stock_target_triggers_generation()
        {
            // Target: ≤2 ready exchanges per enabled show.
            Assert.Fail("pending T286");
        }

        [Fact(Skip = "Pending T286 — see docs/PLAN.md")]
        public static void The_worker_never_generates_or_renders_inside_a_break_window()
        {
            // Off the on-air clock, always — the render fence serves air first.
            Assert.Fail("pending T286");
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
