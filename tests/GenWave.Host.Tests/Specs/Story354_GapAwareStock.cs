// STORY-354 — The stock worker stops burning generations (SPEC F140 · PLAN T328)
//
// BDD specification — xUnit.
//
// gh-#546's evidence: on the-wake-up-call the worker abandoned one in-flight generation
// per 4-minute break cycle, forever — 8–18s of the fenced single-CPU ollama discarded
// each time. The 40→50s duration bump made the funnel converge; this stops the waste.
//
// Two testing surfaces, mirroring Story328_CrosstalkStockWorker.cs's own split:
//
//   - CrosstalkStockPacing (internal, framework-free): the rolling estimate and the abandon
//     backoff, pinned directly — no worker, no timer, no network (ScenarioTheEstimateLearns,
//     ScenarioBackoffBreathes).
//   - A REAL CrosstalkStockWorker, built via CrosstalkWorkerHarness.BuildAsync (Support/, shared
//     with Story328_CrosstalkStockWorker.cs — round-2 review finding "advisory e": a controllable
//     HTTP handler standing in for the LLM backend, a TaskCompletionSource-blocking ITtsSynthesizer
//     standing in for kokoro) for the facts that need to prove the WIRING itself — the runway gate
//     actually stops TickOnceAsync before it reaches the script writer (ScenarioRunwayGatesTheStart),
//     the worker-level backoff gate (ScenarioBackoffGatesTheWorker, round-2 review finding F1), a
//     real mid-flight cancellation actually reaches CrosstalkStockPacing.RecordAbandoned
//     (SadPathTheFenceStaysFree), and a pre-flight refusal never reaches it at all
//     (SadPathAPreFlightRefusalLeavesTheEstimateAlone, round-2 review finding F3).

using GenWave.Host;
using GenWave.Host.Crosstalk;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Host.Tests.Specs;

/// <summary>Minimal <see cref="ILogger"/> that collects every logged message for assertion — mirrors
/// Story356_CovenantPostConfigureWiring.cs's own <c>CapturingLogger&lt;T&gt;</c>, non-generic here
/// because <see cref="CrosstalkStockPacing"/> takes a plain <see cref="ILogger"/> (this folder's own
/// <c>PurgeStaleAssets</c> precedent). Test-scope only.</summary>
file sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}

public static class FeatureGapAwareStock
{
    // ── CrosstalkStockPacing: the pure estimate/backoff pins ───────────────────────────────────
    //
    // The SAME base cadence CrosstalkStockWorker itself wires (its own private TickInterval, 20s) —
    // pinned as a literal here rather than a cross-file read, mirroring
    // Story356_CovenantPostConfigureWiring.cs's own "3s mirrors PlayoutFeederService.PullInterval...
    // pinned here as a literal" precedent one file over.

    static readonly TimeSpan BaseCadence = TimeSpan.FromSeconds(20);
    static readonly DateTimeOffset Now = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    const string ShowSlug = "night-shift";
    const string ShowName = "Night Shift";

    public sealed class ScenarioTheEstimateLearns
    {
        [Fact]
        public void The_estimate_seeds_at_twenty_seconds()
        {
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);

            Assert.Equal(CrosstalkStockPacing.SeedEstimate, pacing.Estimate);
        }

        [Fact]
        public void Completed_generations_update_the_estimate()
        {
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);

            pacing.RecordCompleted(TimeSpan.FromSeconds(40));

            // 50/50 blend of the 20s seed and the 40s sample (this type's own documented algorithm)
            Assert.Equal(TimeSpan.FromSeconds(30), pacing.Estimate);
        }

        /// <summary>Round-2 review advisory (a) — the "only blends upward" half of
        /// <see cref="CrosstalkStockPacing.RecordAbandoned"/>'s own documented algorithm: an abandon
        /// cut off EARLIER than the current estimate (3s against the 20s seed) proves nothing about
        /// how long the attempt would have taken to finish, so it must not erode the estimate toward
        /// zero. The upward half of the SAME algorithm is already proven by
        /// <see cref="FeatureGapAwareStock.SadPathTheFenceStaysFree"/>'s own real-worker fact (a 25s
        /// cancel against the same 20s seed DOES move the estimate) — together the two are the
        /// discrimination pair a "blends every abandon unconditionally" mutant cannot pass.</summary>
        [Fact]
        public void An_abandon_shorter_than_the_estimate_does_not_erode_it()
        {
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);

            pacing.RecordAbandoned(TimeSpan.FromSeconds(3), Now);

            Assert.Equal(CrosstalkStockPacing.SeedEstimate, pacing.Estimate);
        }
    }

    public sealed class ScenarioBackoffBreathes
    {
        /// <summary>The delay a currently-engaged <see cref="CrosstalkStockPacing"/> would wait out —
        /// throws if none is engaged (a test-setup bug, never a legitimate path here), so callers below
        /// stay a plain non-nullable <see cref="TimeSpan"/> read with no null-forgiving operator.</summary>
        static TimeSpan CurrentDelay(CrosstalkStockPacing pacing, DateTimeOffset now) =>
            pacing.BackoffUntil is { } until
                ? until - now
                : throw new InvalidOperationException("expected an engaged backoff");

        [Fact]
        public void Consecutive_abandons_double_the_delay()
        {
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);

            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);
            var firstDelay = CurrentDelay(pacing, Now);

            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);
            var secondDelay = CurrentDelay(pacing, Now);

            Assert.Equal(firstDelay * 2, secondDelay);
        }

        [Fact]
        public void The_delay_caps_at_five_minutes()
        {
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);

            // A long streak — base cadence 20s would otherwise double past the cap well before this
            for (var i = 0; i < 10; i++)
                pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);

            Assert.Equal(CrosstalkStockPacing.MaxBackoff, CurrentDelay(pacing, Now));
        }

        [Fact]
        public void Backoff_engaging_logs_one_line()
        {
            var logger = new CapturingLogger();
            var pacing = new CrosstalkStockPacing(logger, BaseCadence);

            // Three CONSECUTIVE abandons — still one engaged streak, never released in between — must
            // log the engage transition exactly once, not once per abandon (the gh-#558 lesson SPEC
            // F140.4 names explicitly).
            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);
            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);
            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);

            Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        }

        /// <summary>Round-2 review finding F2 — <see cref="CrosstalkStockWorker.RunwaySkipCount"/> is
        /// internal, unreachable from a live daemon; the ENGAGE line folds the same cumulative tally
        /// in (<c>{RunwaySkips}</c>) so an operator without test access to that internal member can
        /// still see it. Three runway skips recorded before the engaging abandon must show up as
        /// "3" in the SAME line <see cref="Backoff_engaging_logs_one_line"/> already proves fires
        /// exactly once — this fact's own job is the line's CONTENT, not its count.</summary>
        [Fact]
        public void Backoff_engaging_reports_the_cumulative_runway_skip_count()
        {
            var logger = new CapturingLogger();
            var pacing = new CrosstalkStockPacing(logger, BaseCadence);

            pacing.RecordRunwaySkip();
            pacing.RecordRunwaySkip();
            pacing.RecordRunwaySkip();

            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);

            var engageLine = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
            Assert.Contains("after 3 runway skips", engageLine.Message);
        }

        [Fact]
        public void A_completion_resets_to_base_cadence()
        {
            // Given a streak already engaged (Consecutive_abandons_double_the_delay above already
            // pins that a single RecordAbandoned call engages it)
            var pacing = new CrosstalkStockPacing(NullLogger.Instance, BaseCadence);
            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);

            pacing.RecordCompleted(TimeSpan.FromSeconds(10));

            Assert.Null(pacing.BackoffUntil);
        }

        /// <summary>Round-2 review finding F4 — the sibling of <see cref="A_completion_resets_to_base_cadence"/>:
        /// the original pending body promised "success releases the backoff with one Information
        /// line", but the implemented fact above only ever asserted the STATE (BackoffUntil==null),
        /// never the log line itself. Deleting <c>CrosstalkStockPacing.Release</c>'s own
        /// <c>LogInformation</c> call survives every other fact in this suite and must go red only
        /// here.</summary>
        [Fact]
        public void A_completion_that_releases_backoff_logs_the_release_line()
        {
            var logger = new CapturingLogger();
            var pacing = new CrosstalkStockPacing(logger, BaseCadence);
            pacing.RecordAbandoned(TimeSpan.FromSeconds(5), Now);

            pacing.RecordCompleted(TimeSpan.FromSeconds(10));

            Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("released"));
        }
    }

    // ── The real worker: the runway gate's own wiring pins ─────────────────────────────────────

    public sealed class ScenarioRunwayGatesTheStart
    {
        /// <summary>The SAME on-air item shape as <c>CrosstalkWorkerHarness.BuildAsync</c>'s own
        /// default, moved close enough to its end that the break window is genuinely CLOSED (100s
        /// since the transition — well past the 30s render budget + 45s margin — and 50s to the
        /// item's own estimated end, past the 45s end-of-item margin too) yet the CLEAR runway to
        /// that seam (50s − 45s margin = 5s) sits well under the 20s seed estimate — the
        /// discrimination BreakWindowOpen's own fixed margin cannot make on its own (SPEC
        /// F140.1).</summary>
        static NowPlayingSnapshot TightRunwaySnapshot(DateTimeOffset now) => new(
            "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromSeconds(100),
            DurationMs: (int)TimeSpan.FromSeconds(150).TotalMilliseconds, IsDrain: false);

        [Fact]
        public async Task No_generation_starts_without_runway()
        {
            var (worker, _, _, nowPlayingService, _, _) = await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);
            nowPlayingService.Update(SingleStation.IdString, TightRunwaySnapshot(Now));

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, worker.RunwaySkipCount);
        }

        [Fact]
        public async Task A_runway_skip_is_counted_not_logged_per_tick()
        {
            var (worker, _, _, nowPlayingService, _, _) = await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);
            nowPlayingService.Update(SingleStation.IdString, TightRunwaySnapshot(Now));

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Two skipped ticks, each counted individually — a one-shot flag (or a per-tick log this
            // fact's own name would have nothing to observe) would not distinguish from one.
            Assert.Equal(2, worker.RunwaySkipCount);
        }

        /// <summary>The positive control: the SAME wiring, comfortably ample runway (the fixture's own
        /// 10-minute item, 5 minutes remaining) — proves the fact above's skip is a real gate, not a
        /// wire that always no-ops regardless of runway.</summary>
        [Fact]
        public async Task A_start_with_runway_proceeds()
        {
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) = await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);

            var tickTask = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(llmHandler.Requests);

            // Cleanup: cancel the in-flight generation so the tick completes, mirroring
            // Story328_CrosstalkStockWorker.cs's own identical cleanup shape.
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3));
            await tickTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    // ── The real worker: the backoff gate's own wiring pins (round-2 review finding F1) ───────────
    //
    // CrosstalkStockWorker.TickOnceAsync's own `if (pacing.IsBackedOff(now)) return;` (SPEC F140.3)
    // has no worker-level fact: deleting it leaves every OTHER fact in this suite green, since
    // DecideAttempt/the runway gate never trip for this show on their own. Both facts below drive a
    // break-window cancellation first (engaging a 40s backoff — base cadence 20s doubled once, the
    // SAME shape Story328_CrosstalkStockWorker.cs's own
    // ScenarioABreakWindowCancellationNeverCostsTheShowACooldown fact already uses to sidestep this
    // exact gate) so there is a real engaged delay to test against.

    public sealed class ScenarioBackoffGatesTheWorker
    {
        /// <summary>The gate itself: a tick landing INSIDE the engaged 40s delay must reach neither
        /// the schedule/now-playing read nor the script writer at all (<c>CrosstalkStockPacing.IsBackedOff</c>'s
        /// own remarks: "does nothing at all") — the request count stays exactly what the FIRST
        /// (cancelled) tick already left it at.</summary>
        [Fact]
        public async Task A_tick_inside_an_engaged_backoff_reaches_no_script_writer()
        {
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) =
                await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);

            // Given the first tick's own generation is cancelled by an opening break window — SPEC
            // F140.3's own "each consecutive abandon" — engaging a 40s backoff from the moment it is
            // recorded
            var firstTick = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3)); // CrosstalkStockWorker's own WatchdogInterval
            await firstTick.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(llmHandler.Requests);

            // The window closes again — DecideAttempt itself would now happily proceed — but the
            // backoff is still engaged
            gate.Exit();

            // When a tick lands well inside the engaged delay (10s of the 40s)
            timeProvider.Advance(TimeSpan.FromSeconds(10));
            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it reaches no script writer at all — the request count is unchanged
            Assert.Single(llmHandler.Requests);
        }

        /// <summary>The positive control: the SAME engaged backoff, advanced past its own delay —
        /// proves the fact above's stall is a real gate, not a wire that always no-ops regardless of
        /// the clock.</summary>
        [Fact]
        public async Task A_tick_after_the_backoff_expires_attempts_again()
        {
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) =
                await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);

            var firstTick = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3));
            await firstTick.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(llmHandler.Requests);

            gate.Exit();
            synthesizer.Reset();

            // When the clock clears the full 40s engaged delay
            timeProvider.Advance(TimeSpan.FromSeconds(40));

            var secondTick = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Then it attempts again — the script writer is called a second time
            Assert.Equal(2, llmHandler.Requests.Count);

            // Cleanup: cancel the second in-flight generation the same way, so nothing outlives the fact
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3));
            await secondTick.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    public sealed class SadPathTheFenceStaysFree
    {
        /// <summary>SPEC F140.2's own "the estimate learns from every cancellation" — live-break copy
        /// still outranks stock (the cancel itself is ALREADY pinned by
        /// Story328_CrosstalkStockWorker.cs's own <c>ScenarioABreakWindowOpeningMidFlightCancelsGeneration</c>;
        /// this fact's own job is the estimate side effect that pin does not cover). The watchdog is
        /// let run past its own 3s poll all the way to 25s of simulated in-flight time — comfortably
        /// past the 20s seed — so the cancellation's own observed elapsed time is genuine NEW evidence
        /// (this type's own remarks: only an elapsed time exceeding the current estimate updates it),
        /// not the sub-estimate 3s a single watchdog poll alone would give.</summary>
        [Fact]
        public async Task A_window_opening_mid_flight_still_cancels()
        {
            var (worker, gate, timeProvider, _, _, synthesizer) = await CrosstalkWorkerHarness.BuildAsync(Now, ShowSlug, ShowName);

            var tickTask = worker.TickOnceAsync(CancellationToken.None);
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            gate.Enter(); // a real on-air render starts mid-flight — live copy outranks stock
            timeProvider.Advance(TimeSpan.FromSeconds(25));
            await tickTask.WaitAsync(TimeSpan.FromSeconds(5));

            // The cancel itself (synthesizer.WasCancelled, llmHandler called once) is already pinned
            // by Story328_CrosstalkStockWorker.cs's own ScenarioABreakWindowOpeningMidFlightCancelsGeneration
            // — this fact's own job is the estimate side effect that pin does not cover.
            Assert.True(worker.EstimatedGenerationTime > CrosstalkStockPacing.SeedEstimate);
        }
    }

    public sealed class SadPathAPreFlightRefusalLeavesTheEstimateAlone
    {
        /// <summary>Round-2 review finding F3 (production bug) — <c>Llm:Endpoint</c> unset makes
        /// <c>CrosstalkScriptWriter</c> discard in milliseconds, WITHOUT running a generation at all
        /// (<c>CrosstalkWriteResult.Discarded</c>'s own <c>GenerationAttempted: false</c>).
        /// <c>CrosstalkStockWorker.RecordPacingOutcome</c> must leave the rolling estimate alone for
        /// an outcome like this — blending its near-zero elapsed time in would erode the estimate
        /// toward zero on every tick of an outage, exactly when the runway gate most needs an honest
        /// number. Positive control: <see cref="FeatureGapAwareStock.ScenarioTheEstimateLearns.Completed_generations_update_the_estimate"/>
        /// already proves <c>RecordCompleted</c>'s own blend is real, not a no-op — this fact's job is
        /// proving THIS outcome never reaches it.</summary>
        [Fact]
        public async Task A_millisecond_pre_flight_refusal_leaves_the_estimate_untouched()
        {
            var (worker, _, _, _, llmHandler, _) = await CrosstalkWorkerHarness.BuildAsync(
                Now, ShowSlug, ShowName, llmEndpoint: "");

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then no request even reached the wire (the pre-flight short-circuit) and the estimate
            // stayed exactly at its seed — no erosion toward zero
            Assert.Empty(llmHandler.Requests);
            Assert.Equal(CrosstalkStockPacing.SeedEstimate, worker.EstimatedGenerationTime);
        }
    }
}
