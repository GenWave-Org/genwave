using Microsoft.Extensions.Options;
using GenWave.Context;
using GenWave.Host.Options;
using GenWave.Orchestration;

namespace GenWave.Host.Playout;

/// <summary>
/// The Host's one wall-clock actor for the context seam (SPEC F107.3, STORY-297, PLAN T226):
/// advances <see cref="ContextPipeline"/> on a fixed interval and enqueues whatever it reports due
/// into the SAME <see cref="SpeechDeferralQueue"/> <c>Orchestrator</c> drains at track boundaries
/// (F74.1). Deliberately dumb — it owns no cadence logic of its own (that is
/// <see cref="ContextPipeline"/>'s job, SPEC F107.2's fetch-once-per-slot rule); it only calls in
/// more often than any provider's own cadence could possibly need (F108.2's 30-minute floor is the
/// tightest today).
///
/// <para>
/// <b>Deliberately NOT under <c>GenWave.Host.Context</c></b> — that namespace is reserved forever
/// by the L5 graduation tripwire (SPEC F105.4, gh-#378: "this subsystem is born OUTSIDE Host";
/// <c>GenWave.Context</c> itself is where F107's logic lives). This class is a thin Host-side
/// scheduling shell, the exact shape as <c>Stats.ListenerStatsPollerService</c> one folder over — it
/// lives alongside <see cref="PlayoutFeederService"/> instead, because the queue it feeds
/// (<see cref="SpeechDeferralQueue"/>) is what the Orchestrator ultimately drains on that same
/// feeder's behalf.
/// </para>
///
/// <para>
/// <b>Sequential, never overlapping (review ruling).</b> One <see cref="PeriodicTimer"/> await-loop,
/// the same shape as <c>Stats.ListenerStatsPollerService</c>: each tick — including every enqueue
/// below — is fully awaited before the next tick's wait even begins, so two ticks can never run
/// concurrently. This matters because <see cref="ContextPipeline"/>'s own thread-safety contract
/// (its class remarks) is scoped to exactly two concurrent callers — THIS ticker and the
/// copywriter's <see cref="ContextPipeline.TryTakeDuePatterFact"/> lane — never ticker-vs-ticker; a
/// second concurrent tick was never a case that contract was built to cover.
/// </para>
///
/// <para>
/// <b>Never crashes the host.</b> A tick that throws is logged and the loop continues — an
/// unavailable/misbehaving context provider, or a bug in this class's own enqueue step, must never
/// take down the whole broadcast process the way an unhandled <see cref="BackgroundService"/>
/// exception would.
/// </para>
///
/// <para>
/// <b>One boundary late, by design (F7 fix, T226 review).</b> This is the "future wall-clock
/// producer" ARCHITECTURE.md's speech-deferral-queue section names ahead of time (SPEC F74.1): its
/// own one-ahead planning buffer means an enqueue from this ticker can land one audible boundary
/// later than the instant it actually became due.
/// </para>
/// </summary>
sealed class ContextTickerService(
    ContextPipeline pipeline,
    SpeechDeferralQueue deferralQueue,
    IOptions<ContextTickerOptions> options,
    ILogger<ContextTickerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromSeconds(options.Value.TickIntervalSeconds);
        logger.LogInformation("Context ticker started: every {TickIntervalSeconds}s", tickInterval.TotalSeconds);

        try
        {
            using var timer = new PeriodicTimer(tickInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Context ticker stopped");
    }

    /// <summary>One tick: advance the pipeline, enqueue whatever came due. A thrown exception here
    /// is logged and swallowed — see this class's own "never crashes the host" remarks. Internal so
    /// a test can drive exactly one tick without waiting on the real timer.</summary>
    internal async Task TickOnceAsync(CancellationToken ct)
    {
        try
        {
            var due = await pipeline.TickAsync(ct).ConfigureAwait(false);
            foreach (var segment in due)
            {
                // due.Key doubles as the deferral queue's per-(kind, discriminator) supersede key
                // (F107.4) — a due weather fact and a due history fact coexist instead of one
                // silently discarding the other. Content is captured now, at enqueue time, and
                // carried verbatim to drain (SpeechDeferral.Context's own remarks) — the Orchestrator
                // re-checks freshness against ContextContent.FreshUntil at drain time regardless.
                deferralQueue.Enqueue(
                    SpeechDeferralKind.Context,
                    reason: $"context: {segment.Key} came due",
                    discriminator: segment.Key,
                    context: segment.Content);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancellation (shutdown) — not a tick fault, must propagate to stop the loop.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Context ticker tick failed; continuing on the next tick");
        }
    }
}
