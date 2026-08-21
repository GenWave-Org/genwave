// STORY-328 — Stocked ahead, aired once: the Host stock-timer shell (F127.7, PLAN T286)
//
// BDD specification — xUnit. CrosstalkStockWorker is deliberately thin (the feeder-pattern
// precedent: logic stays in the planner/decider, the Host owns only the timer shell), so its
// self-run-required pins target the exact seams the worker itself exposes for that reason:
//
//   - DecideAttempt (internal static): the "is a generation attempt even worth starting" gate,
//     testable with a plain CrosstalkPlanner + explicit OnAirSnapshot/NowPlayingSnapshot/now —
//     none of CrosstalkScriptWriter/CrosstalkAssembler's own network/ffmpeg dependencies, i.e.
//     testable without a real ollama (PLAN T286's own requirement). The break-window fact below is
//     a discrimination pair (open ⇒ null / closed ⇒ non-null) so neither half reads true regardless.
//   - PurgeStaleAssets (internal static): the startup-purge rider (the T284/T285-recorded rider for
//     T286), pinned directly against a temp directory — no DI graph, no BackgroundService host needed.
//
// PLAN T286 review (F1/F2/F4): three additions beyond the original two pins —
//
//   - DecideAttempt gained refillInFlight/renderBudget/cooldownUntil parameters (F1's real signal
//     and F4's cooldown); the facts below cover the wiring, not the underlying decision math
//     (CrosstalkBreakWindow's own three-face math is pinned in GenWave.Orchestration.Tests).
//   - Two worker-level facts, driving the REAL CrosstalkStockWorker/CrosstalkScriptWriter/
//     CrosstalkAssembler end to end via CrosstalkWorkerHarness.BuildAsync (Support/, shared with
//     Story354_GapAwareStock.cs — SPEC F140/T328 round-2 review finding "advisory e": a controllable
//     HTTP handler standing in for the LLM backend, a TaskCompletionSource-blocking ITtsSynthesizer
//     standing in for kokoro): "never generates inside a break window" (the worker-behavior half of
//     the scaffold's original placeholder — see the relocation note in
//     GenWave.Orchestration.Tests/Specs/Story328_StockedAheadAiredOnce.cs) and "a break window
//     opening mid-flight cancels the in-flight generation" (F2's own required fact, driven with
//     FakeTimeProvider so the watchdog's PeriodicTimer never waits on real wall-clock time).

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Crosstalk;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Host.Tests.Specs;

// ── In-file fixtures ────────────────────────────────────────────────────────────────────────────
//
// Minimal, local IPersonaStore/ICrosstalkScopeProvider doubles — DecideAttempt never reaches
// IPersonaStore at all (it never calls CrosstalkPlanner.TryCastAsync), so every member below is a
// throwing stub except what CrosstalkPlanner's constructor merely needs to exist.

file sealed class NeverCalledPersonaStore : IPersonaStore
{
    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class FakeCrosstalkScopeProvider(IReadOnlyList<string> enabledShows) : ICrosstalkScopeProvider
{
    public IReadOnlyList<string> EnabledShows { get; set; } = enabledShows;
    public int EveryNthAiring => 1;
}

// ── Worker-level fixtures (PLAN T286 review F1/F2) ─────────────────────────────────────────────────
//
// The two facts below (ScenarioTheWorkerNeverGeneratesInsideABreakWindow,
// ScenarioABreakWindowOpeningMidFlightCancelsGeneration) construct a REAL CrosstalkStockWorker via
// CrosstalkWorkerHarness.BuildAsync (Support/, shared with Story354_GapAwareStock.cs — round-2
// review finding "advisory e") — real CrosstalkPlanner/CrosstalkScriptWriter/CrosstalkAssembler/
// CachingScheduleResolver/ScheduleResolver, with only the external edges faked: an HTTP handler
// standing in for the LLM backend and an ITtsSynthesizer standing in for kokoro, mirroring Story012's
// own "real orchestrator, controllable stub HTTP server" idiom one project over.

public static class FeatureCrosstalkStockWorker
{
    static readonly TimeSpan DefaultRenderBudget = TimeSpan.FromSeconds(30);
    static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCooldowns =
        new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    static ScheduleSegment Segment(long? id, long? personaId) =>
        new(Id: id, Day: DayOfWeek.Monday, StartMinute: 480, EndMinute: 960, PersonaId: personaId,
            Genres: null, EnergyMin: null, EnergyMax: null);

    static ShowSummary Show(string slug) => new(1, "Morning Drive", null, null) { Slug = slug };

    static OnAirSnapshot OnAir(string showSlug) =>
        new(Segment(1, 10), PersonaId: 10, SegmentEnvelope.StationDefault, BoundaryAt: null, NextSegment: null, Show: Show(showSlug));

    static CrosstalkPlanner MakePlanner(IReadOnlyList<string> enabledShows) =>
        new(new NeverCalledPersonaStore(), new FakeCrosstalkScopeProvider(enabledShows), NullLogger<CrosstalkPlanner>.Instance);

    /// <summary>Convenience wrapper over <see cref="CrosstalkStockWorker.DecideAttempt"/> defaulting
    /// PLAN T286 review F1/F4's three new parameters to "nothing extra blocks" — no render in flight,
    /// the default render budget, no cooldowns — so every pre-existing fact below still isolates
    /// exactly the same fact it always did; only the facts that mean to exercise one of the three new
    /// parameters pass it explicitly.</summary>
    static CrosstalkStockAttempt? Decide(
        CrosstalkPlanner planner, OnAirSnapshot? onAir, NowPlayingSnapshot? nowPlaying, DateTimeOffset now,
        bool refillInFlight = false, IReadOnlyDictionary<string, DateTimeOffset>? cooldownUntil = null) =>
        CrosstalkStockWorker.DecideAttempt(
            planner, onAir, nowPlaying, now, refillInFlight, DefaultRenderBudget, cooldownUntil ?? NoCooldowns);

    // ── DecideAttempt: the never-in-window pin ─────────────────────────────────────────────────

    public sealed class ScenarioTheGateNeverAttemptsInsideABreakWindow
    {
        [Fact]
        public void An_open_break_window_blocks_an_otherwise_eligible_attempt()
        {
            // Given an enabled show below its stock target — every OTHER gate would pass — but the
            // current on-air item's estimated end sits inside CrosstalkBreakWindow's own safety
            // margin, AND it started long enough ago to be clear of the after-transition face too —
            // isolating the end-of-item margin face on its own
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
                DurationMs: (int)TimeSpan.FromMinutes(5).TotalMilliseconds + 10_000, IsDrain: false);

            var attempt = Decide(planner, onAir, nowPlaying, now);

            // Then no attempt is decided — the render fence serves air first
            Assert.Null(attempt);
        }

        /// <summary>The positive control: the SAME eligible show/planner state, with the on-air
        /// item comfortably clear of BOTH the after-transition window and the end-of-item margin —
        /// a genuine mid-item quiet stretch — proves the fact above's "null" is a real gate, not a
        /// wire that always no-ops regardless of the window.</summary>
        [Fact]
        public void A_closed_break_window_lets_an_eligible_attempt_through()
        {
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
                DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false);

            var attempt = Decide(planner, onAir, nowPlaying, now);

            Assert.NotNull(attempt);
            Assert.Equal("morning-drive", attempt.ShowSlug);
        }

        [Fact]
        public void An_unknown_on_air_duration_reads_as_an_open_break_window()
        {
            // Given no NowPlayingSnapshot at all (the process boot window, no tick has published yet)
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var attempt = Decide(planner, onAir, nowPlaying: null, now);

            // Then no attempt — an unknown state is never read as permission to generate
            Assert.Null(attempt);
        }

        // ── PLAN T286 review F1: refillInFlight threads through DecideAttempt ──────────────────

        [Fact]
        public void A_refill_in_flight_blocks_an_otherwise_eligible_attempt_even_mid_item()
        {
            // Given the SAME mid-item quiet stretch the positive control above proves eligible —
            // but a real on-air render is in flight right now
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
                DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false);

            var attempt = Decide(planner, onAir, nowPlaying, now, refillInFlight: true);

            Assert.Null(attempt);
        }

        // ── PLAN T286 review F4: cooldownUntil threads through DecideAttempt ───────────────────

        [Fact]
        public void A_show_still_inside_its_cooldown_never_attempts()
        {
            // Given the SAME mid-item quiet stretch/otherwise-eligible show, but this show's own
            // cooldown (set by TickOnceAsync after a prior discard) has not expired yet
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
                DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false);
            var cooldowns = new Dictionary<string, DateTimeOffset> { ["morning-drive"] = now + TimeSpan.FromSeconds(1) };

            var attempt = Decide(planner, onAir, nowPlaying, now, cooldownUntil: cooldowns);

            Assert.Null(attempt);
        }

        /// <summary>The positive control: the SAME cooldown entry, but already expired — proves the
        /// fact above is a real gate, not a wire that always blocks regardless of the timestamp.</summary>
        [Fact]
        public void A_show_whose_cooldown_has_expired_attempts_again()
        {
            var planner = MakePlanner(["morning-drive"]);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
                DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false);
            var cooldowns = new Dictionary<string, DateTimeOffset> { ["morning-drive"] = now - TimeSpan.FromSeconds(1) };

            var attempt = Decide(planner, onAir, nowPlaying, now, cooldownUntil: cooldowns);

            Assert.NotNull(attempt);
        }
    }

    public sealed class ScenarioTheGateAlsoRespectsScopeAndStock
    {
        [Fact]
        public void A_show_not_named_in_Crosstalk_Shows_never_attempts()
        {
            var planner = MakePlanner(enabledShows: []);
            var onAir = OnAir("morning-drive");
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var nowPlaying = new NowPlayingSnapshot(
                "track:1", "Title", "Artist", GainDb: 0, StartedAt: now, DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false);

            var attempt = Decide(planner, onAir, nowPlaying, now);

            Assert.Null(attempt);
        }

        [Fact]
        public void A_grid_gap_with_no_on_air_segment_never_attempts()
        {
            var planner = MakePlanner(["morning-drive"]);
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var attempt = Decide(planner, onAir: null, nowPlaying: null, now);

            Assert.Null(attempt);
        }
    }

    // ── PurgeStaleAssets: the startup-purge pin ────────────────────────────────────────────────

    public sealed class ScenarioTheStartupPurge
    {
        [Fact]
        public void Files_left_over_from_a_previous_run_are_deleted()
        {
            // Given a crosstalk cache directory holding an asset an earlier, crashed/restarted
            // process assembled but never vended (SPEC F127.7's own "the stock survives nothing" —
            // PLAN T285's recorded rider: crosstalk/ has no other sweeper)
            var dir = Directory.CreateTempSubdirectory("crosstalk-purge-test-").FullName;
            try
            {
                var orphan = Path.Combine(dir, "orphan.wav");
                File.WriteAllBytes(orphan, [0]);

                CrosstalkStockWorker.PurgeStaleAssets(dir, NullLogger.Instance);

                // Then the orphaned asset is gone
                Assert.False(File.Exists(orphan));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void A_directory_that_does_not_exist_yet_is_a_no_op()
        {
            // Given a fresh install/volume — CrosstalkAssembler creates the directory lazily on its
            // own first write, so it may simply not exist yet at worker boot
            var dir = Path.Combine(Path.GetTempPath(), $"crosstalk-purge-test-missing-{Guid.NewGuid():N}");

            var exception = Record.Exception(() => CrosstalkStockWorker.PurgeStaleAssets(dir, NullLogger.Instance));

            Assert.Null(exception);
        }
    }

    // ── Worker-level: the real CrosstalkStockWorker (PLAN T286 review F1/F2) ──────────────────────
    //
    // CrosstalkWorkerHarness.BuildAsync (Support/, round-2 review finding "advisory e") builds the
    // REAL CrosstalkStockWorker every fact below drives — see that type's own remarks for exactly
    // what it fakes (an HTTP handler standing in for the LLM backend, a BlockingTtsSynthesizer
    // standing in for kokoro) and why. Every fact here seats the SAME show, "morning-drive".

    const string ShowSlug = "morning-drive";
    const string ShowName = "Morning Drive";

    public sealed class ScenarioTheWorkerNeverGeneratesInsideABreakWindow
    {
        /// <summary>The worker-BEHAVIOR half of the scaffold's original
        /// <c>The_worker_never_generates_or_renders_inside_a_break_window</c> placeholder (see the
        /// relocation note in GenWave.Orchestration.Tests/Specs/Story328_StockedAheadAiredOnce.cs,
        /// ScenarioNeverInsideABreakWindow) — drives the REAL worker end to end and proves it never
        /// even reaches the script writer while a break window is open (here: a real on-air render
        /// in flight, PLAN T286 review F1's own real signal). Paired with
        /// <see cref="FeatureCrosstalkStockWorker.ScenarioABreakWindowOpeningMidFlightCancelsGeneration"/>'s
        /// own first assertion (proving the SAME wiring DOES call the writer once when the window is
        /// closed) as the discrimination pair this fact alone cannot be — an always-empty wire would
        /// pass this fact vacuously.</summary>
        [Fact]
        public async Task An_open_break_window_stops_the_tick_before_any_script_writer_call()
        {
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, gate, _, _, llmHandler, _) = await CrosstalkWorkerHarness.BuildAsync(now, ShowSlug, ShowName);
            gate.Enter(); // a real on-air render is in flight right now

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(llmHandler.Requests);
        }
    }

    public sealed class ScenarioABreakWindowOpeningMidFlightCancelsGeneration
    {
        /// <summary>PLAN T286 review F2's own required fact. The window starts CLOSED (so the tick
        /// legitimately reaches the script writer — the positive-control half of the pair with the
        /// scenario above) and generation blocks inside the assembler's own first per-line synth
        /// call; a real on-air render then starts (<see cref="OnAirRenderGate.Enter"/>) while it is
        /// still in flight, and the watchdog's next poll (driven forward with
        /// <see cref="FakeTimeProvider.Advance(TimeSpan)"/> — no wall-clock wait) must cancel it. The
        /// fake synthesizer never resolves its own completion source on its own, so the tick task
        /// completing at all (bounded by <see cref="Task.WaitAsync(TimeSpan)"/>'s own timeout, never
        /// hanging the suite) IS the proof cancellation actually reached the in-flight work.</summary>
        [Fact]
        public async Task An_in_flight_generation_is_cancelled_the_instant_the_window_reopens()
        {
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) = await CrosstalkWorkerHarness.BuildAsync(now, ShowSlug, ShowName);

            var tickTask = worker.TickOnceAsync(CancellationToken.None);

            // Then generation genuinely started — the script writer was called, and the assembler is
            // now blocked in its own first per-line synth (the positive control half of the pair)
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(llmHandler.Requests);

            // When a real on-air render starts mid-flight and the watchdog's next poll observes it
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3)); // CrosstalkStockWorker's own WatchdogInterval

            // Then the in-flight generation is cancelled — the tick completes (never hangs) and the
            // synthesizer's own token was genuinely cancelled, not merely abandoned
            await tickTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(synthesizer.WasCancelled);
        }
    }

    // ── PLAN T286 review F1 (BLOCKING): the cooldown write + discrimination pin ────────────────────
    //
    // CrosstalkStockWorker.TickOnceAsync only charges DiscardCooldown for a GENUINE discard, never a
    // break-window cancellation (see that method's own remarks) — a rule with two ways to go silently
    // unpinned: deleting the write entirely (a persistently-failing show floods the LLM every tick),
    // or inverting which outcome charges it (a break-window cancel — retried off-window BY DESIGN —
    // starts sitting out its own show unnecessarily). The two facts below drive the worker through
    // TWO real ticks each, using the SAME CrosstalkWorkerHarness.BuildAsync fixture as the pair above, so the SECOND
    // tick's own llmHandler.Requests.Count is the observable proof of which outcome actually happened.

    public sealed class ScenarioAGenuineDiscardCostsTheShowACooldown
    {
        // One speaker-tagged line — CrosstalkScriptParser.MinLines is 3, so this reply is a genuine
        // discard (never reaches the assembler/synthesizer at all), not a break-window cancellation.
        const string UnparseableReply = "HOST: Hi there.";

        /// <summary>The genuine-discard half of the F4 pin. The first tick's own reply is unparseable
        /// (an immediate <c>CrosstalkWriteResult.Discarded</c>, no break window involved at all), so
        /// <c>TickOnceAsync</c> writes <c>cooldownUntil["morning-drive"]</c>; the second tick — called
        /// immediately after, the clock never advanced — must then find that show still cooling down
        /// and skip it entirely. Neuter check: deleting the cooldown write survives this fact if the
        /// second tick is never actually driven, so both ticks run for real here, and the ONLY
        /// observable is the request count the second tick would have added to had it attempted
        /// again.</summary>
        [Fact]
        public async Task A_discarded_generation_skips_its_show_on_the_very_next_tick()
        {
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, _, _, _, llmHandler, _) = await CrosstalkWorkerHarness.BuildAsync(now, ShowSlug, ShowName, UnparseableReply);

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the second tick never re-attempted this show — a cooldown, not a coincidence: with
            // no cooldown the second tick would have made its own request too
            Assert.Single(llmHandler.Requests);
        }
    }

    public sealed class ScenarioABreakWindowCancellationNeverCostsTheShowACooldown
    {
        /// <summary>The cancel-does-not-cost half of the F4 pin — the mirror image of the fact above,
        /// and the discrimination partner that proves it is not vacuous (an always-cooldown mutant
        /// would pass the fact above but red here; an always-off mutant would pass here but red
        /// above). The first tick is cancelled by an opening break window (the exact
        /// <see cref="OnAirRenderGate.Enter"/> + <see cref="FakeTimeProvider.Advance(TimeSpan)"/> shape
        /// as <see cref="FeatureCrosstalkStockWorker.ScenarioABreakWindowOpeningMidFlightCancelsGeneration"/>'s
        /// own fact); the window then closes again and a second tick must reach the script writer a
        /// SECOND time — a cooldown wrongly charged for the cancellation would skip it instead. Neuter
        /// check: inverting the discrimination (charging the cooldown on a break-window cancel rather
        /// than a discard) reds this fact — the second tick's request would never fire.</summary>
        [Fact]
        public async Task A_cancelled_generation_re_attempts_its_show_on_the_next_tick()
        {
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) = await CrosstalkWorkerHarness.BuildAsync(now, ShowSlug, ShowName);

            var firstTick = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            gate.Enter(); // a real on-air render starts mid-flight
            timeProvider.Advance(TimeSpan.FromSeconds(3)); // CrosstalkStockWorker's own WatchdogInterval
            await firstTick.WaitAsync(TimeSpan.FromSeconds(5));

            // The window closes again and the fake is re-armed to observe the SECOND attempt's own
            // synth call, distinct from the first's already-completed one
            gate.Exit();
            synthesizer.Reset();

            // SPEC F140.3 (PLAN T328, added after this fact was first written): the FIRST tick's own
            // cancellation is also an "abandon" for CrosstalkStockPacing's own backoff — one abandon
            // engages a 40s delay (base cadence 20s, doubled once) from the moment it was recorded.
            // Advancing past it here is orthogonal to what THIS fact pins (the per-show cooldown, a
            // different mechanism entirely — see this class's own remarks) and does not touch it.
            timeProvider.Advance(TimeSpan.FromSeconds(40));

            var secondTick = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Cleanup: cancel the second in-flight generation the same way, so nothing outlives the fact
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3));
            await secondTick.WaitAsync(TimeSpan.FromSeconds(5));

            // Then the second tick genuinely re-attempted this show — no cooldown was charged for a
            // break-window cancellation
            Assert.Equal(2, llmHandler.Requests.Count);
        }
    }
}
