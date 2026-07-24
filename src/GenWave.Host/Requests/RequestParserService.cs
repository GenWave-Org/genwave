namespace GenWave.Host.Requests;

using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Tts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Channel-fed background parser (SPEC F87.4, STORY-225, PLAN T88): turns a pending request's wish
/// into structured predicates (artist/title/moods) and writes the outcome back through
/// <see cref="IRequestStore.MarkParsedAsync"/>. <see cref="ParseOneAsync"/> then folds catalog
/// matching (SPEC F87.5, STORY-226, PLAN T89) into the SAME pipeline right after that write, handing
/// off to <see cref="RequestMatcher"/> — a second hosted service would need its own feed/discriminator
/// over <c>station.request</c> for zero benefit, since matching needs exactly the predicates this
/// method already just parsed.
///
/// <para>
/// Mode is read fresh PER WISH, at the moment of parse — never cached across requests or across this
/// service's own lifetime (<see cref="IDegradationModeReader.CurrentMode"/> is a plain, side-effect-free
/// read, the same narrow seam <see cref="LlmCopyWriter"/> itself reads per render) — so a live
/// degradation transition, or an operator's <c>Llm:DegradationPin</c> edit, applies to the very next
/// wish, never waiting for a restart. Only <see cref="DegradationMode.Normal"/> WITH a configured
/// <c>Llm:Endpoint</c> routes to <see cref="LlmWishParser"/>; every other combination (Soft, Hard, or
/// an empty endpoint even if reported Normal) routes to <see cref="DeterministicWishParser"/> — the
/// endpoint check is a deliberate belt-and-suspenders alongside the mode check, since
/// <c>DegradationController</c> only reflects "unconfigured ⇒ Hard" once something has evaluated it
/// at least once since boot (F69 must never depend on that timing).
/// </para>
///
/// <para>
/// Two producers feed the SAME queue (mirrors <c>EnrichmentService</c>'s own scan-plus-recovery
/// shape): <see cref="Api.SpectatorRequestsController"/> TryWrites the fresh id at insert time for a
/// prompt parse, and <see cref="RecoverPendingAsync"/> replays any row a previous run's crash/restart
/// left unparsed — the in-memory queue does not survive a restart. Recovery runs once, at startup,
/// before the live queue is drained; a row it re-queues that has ALREADY been parsed since (a narrow
/// race with a concurrent live insert) is simply a harmless no-op read, since
/// <see cref="IRequestStore.GetForParseAsync"/> returns <see langword="null"/> for anything no longer
/// a legal parse target (see that member's own remarks).
/// </para>
///
/// <para>
/// Wish text is NEVER logged, at any level, by this class or either <see cref="IWishParser"/>
/// implementation (SPEC F87.8) — every log line here carries ids, modes, and outcomes only. The one
/// place wish text legitimately transits is the LLM REQUEST BODY itself
/// (<see cref="LlmWishParser"/>) — that traffic additionally reaches <c>LlmCallRing</c>, the
/// admin-only call inspector (SPEC F73), a distinct, already-audited, never-persisted disclosure
/// boundary this class has nothing to do with.
/// </para>
/// </summary>
sealed class RequestParserService(
    ChannelReader<long> queue,
    IRequestStore store,
    LlmWishParser llmParser,
    DeterministicWishParser deterministicParser,
    IDegradationModeReader degradationMode,
    IOptionsMonitor<LlmOptions> llmOptions,
    RequestMatcher matcher,
    ILogger<RequestParserService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // don't block host startup

        await RecoverPendingAsync(stoppingToken);

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
            await ParseOneAsync(id, stoppingToken);
    }

    /// <summary>
    /// Startup-only recovery sweep (SPEC F87.4) — internal for deterministic testing, mirrors
    /// <c>EnrichmentService.RequeuePendingAsync</c>'s own role/shape exactly.
    /// </summary>
    internal async Task RecoverPendingAsync(CancellationToken ct)
    {
        try
        {
            var pending = await store.ListUnparsedPendingIdsAsync(ct);
            if (pending.Count == 0) return;

            logger.LogInformation("Recovering {Count} unparsed request(s) from a previous run", pending.Count);
            foreach (var id in pending)
                await ParseOneAsync(id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // A failed recovery query must not take the service down — a live insert still feeds the
            // queue directly; the next restart's recovery sweep retries whatever this one missed.
            logger.LogError(ex, "Failed to recover pending request parses on startup");
        }
    }

    /// <summary>
    /// The real per-id work — internal for deterministic testing, mirrors
    /// <c>EnrichmentService.EnrichOneAsync</c>'s own directly-testable-seam role.
    /// </summary>
    internal async Task ParseOneAsync(long id, CancellationToken ct)
    {
        try
        {
            var request = await store.GetForParseAsync(id, ct);
            if (request is null) return; // already parsed, expired, evicted, or gone since it was queued

            var mode = degradationMode.CurrentMode;
            var useLlm = mode == DegradationMode.Normal && !string.IsNullOrEmpty(llmOptions.CurrentValue.Endpoint);
            IWishParser parser = useLlm ? llmParser : deterministicParser;

            var parsed = await parser.ParseAsync(request.Wish, ct);
            await store.MarkParsedAsync(id, parsed.Artist, parsed.Title, parsed.Moods, unmatched: parsed.IsEmpty, ct);

            // Matching (SPEC F87.5, PLAN T89) continues the SAME pipeline right here — an empty parse
            // is already unmatched (the write above), so RequestMatcher is only ever reached with a
            // predicate actually worth probing the catalog for.
            if (!parsed.IsEmpty)
                await matcher.MatchAsync(id, parsed.Artist, parsed.Title, parsed.Moods, ct);

            logger.LogInformation(
                "Parsed request {Id} via {Parser}: {Outcome}",
                id, useLlm ? "LLM" : "deterministic", parsed.IsEmpty ? "no predicates" : "predicates found");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // let the loop unwind on shutdown
        }
        catch (Exception ex)
        {
            // Defense-in-depth only — IWishParser never throws past its own boundary (both
            // implementations collapse every failure internally). A store failure here (a dropped
            // connection) must not crash the loop; the row stays unparsed and is retried by the next
            // restart's recovery sweep.
            logger.LogError(ex, "Failed to parse request {Id}", id);
        }
    }
}
