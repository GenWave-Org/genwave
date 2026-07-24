namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

/// <summary>
/// The real <see cref="IRequestFulfillmentSource"/> (SPEC F87.6, STORY-227, PLAN T90). Sweeps stale
/// pending rows to <c>expired</c> first (opportunistic, cheap indexed UPDATE — no dedicated timer),
/// then asks <paramref name="store"/> for the oldest live pending request and resolves it against
/// <paramref name="probe"/>'s law-and-optionally-envelope predicate, governed by
/// <paramref name="overrideProvider"/>'s live <c>Station:Requests:OverrideEnvelope</c> value (read
/// fresh on every call — never cached — the same F30.1 discipline every sibling live-setting seam
/// follows).
///
/// <para>
/// <b>Law, never bypassed (SPEC F87.6):</b> <see cref="IRequestCatalogProbe.GetSelectableByIdAsync"/>
/// and <see cref="IRequestCatalogProbe.FindVibeAsync"/> both re-check ready/measurable/eligible/not-
/// never-play (plus the gh-#99 safe-scope exclusion) unconditionally, regardless of
/// <c>OverrideEnvelope</c> — an operator veto flip after the T89 match silently idles the request to
/// expiry rather than airing it (T89 parity).
/// </para>
///
/// <para>
/// <b>Envelope, mode-dependent:</b> <c>OverrideEnvelope=true</c> (default) passes <see langword="null"/>
/// for the probe's envelope argument — genre/energy are bypassed entirely. Rotation-recency is never
/// consulted by this seam in EITHER mode: a fulfillment resolves one already-identified id (a specific
/// catalog match) or a single vibe pick, never a competitive pool — there is no "prefer a fresher
/// candidate" tier here to relax in the first place. <c>OverrideEnvelope=false</c> passes
/// <paramref name="envelope"/> through unchanged; a candidate that fails it makes this call return
/// <see langword="null"/> — the request simply stays pending, retried on the next pick, until it
/// either satisfies the envelope or its window elapses (SPEC F87.6's "idles to expiry").
/// </para>
///
/// <para>
/// <b>One-shot (SPEC F87.6):</b> the CAS <see cref="IRequestStore.TryMarkFulfilledAsync"/> stamps
/// <c>pending → fulfilled</c> the instant this rung SELECTS the winning candidate — before
/// <see cref="Orchestrator"/> ever returns it to the feeder. Simplest honest choice: plumbing a
/// push-time callback back into this seam so the stamp lands only once the feeder actually pushes
/// would be a materially bigger diff to save an exotic edge case (a push that silently fails after a
/// successful pick) this station's scale does not justify — documented here rather than hidden.
/// </para>
/// </summary>
public sealed class RequestFulfillmentProvider(
    IRequestStore store,
    IRequestCatalogProbe probe,
    IRequestOverrideEnvelopeProvider overrideProvider,
    IStationEventSink events,
    TimeProvider timeProvider) : IRequestFulfillmentSource
{
    /// <inheritdoc/>
    public async Task<RequestFulfillment?> TryFulfillAsync(SegmentEnvelope envelope, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // Opportunistic expiry sweep (SPEC F87.6) — cheap indexed UPDATE, run at the top of every
        // attempt rather than on a dedicated timer. One payload-free RequestExpired event per row
        // actually expired (never one summary event) — see that event's own remarks.
        var expiredCount = await store.ExpireStaleAsync(now, ct);
        for (var i = 0; i < expiredCount; i++)
            events.Publish(new RequestExpired());

        var request = await store.GetOldestLiveAsync(now, ct);
        if (request is null) return null;

        var effectiveEnvelope = overrideProvider.Current ? null : envelope;

        MediaReference? media;
        bool wasVibe;
        if (request.MatchedMediaId is { } mediaId)
        {
            wasVibe = false;
            media = await probe.GetSelectableByIdAsync(mediaId, effectiveEnvelope, ct);
        }
        else if (request.Moods.Count > 0)
        {
            wasVibe = true;
            media = await probe.FindVibeAsync(request.Moods, effectiveEnvelope, ct);
        }
        else
        {
            // Defensive only — IRequestStore.GetOldestLiveAsync's own contract never returns a row
            // with neither a match nor a mood predicate (see that member's remarks).
            return null;
        }

        if (media is null) return null; // vetoed, off-envelope, or gone — idles to expiry (SPEC F87.6)

        if (!await store.TryMarkFulfilledAsync(request.Id, ct))
            return null; // lost the one-shot CAS — a concurrent attempt already claimed this row

        events.Publish(new RequestFulfilled());

        var candidate = new RotationCandidate(
            media, RepeatedRecent: false, RepeatedArtist: false, Energy: null, PersonaPick: null, RequestFulfilled: true);
        return new RequestFulfillment(candidate, request.Id, wasVibe);
    }
}
