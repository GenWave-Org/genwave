using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Artwork;

/// <summary>
/// SPEC F131.2/F131.3 (PLAN T298/T307 review rider — MANDATORY) — memoizes the single-row station
/// image on a ≤30s TTL so every reader that would otherwise hit <see cref="IStationImageStore"/>
/// directly shares ONE bounded read instead: the dj-route fallback (<c>SpectatorArtworkController</c>'s
/// own <c>ServeStationImageAsync</c>), the new station token route
/// (<c>SpectatorArtworkController.GetStationArtwork</c>), the F88 artwork fallback
/// (<c>SpectatorArtworkController.GetArtwork</c>, unified onto the same ladder at T307), the
/// spectator favicon/logo route(s) (<see cref="Api.SpectatorPageEndpoints"/>), and the feeder push
/// path (<see cref="Engine.ArtworkUrlResolver"/>). Without this memo, an anonymous prober hammering
/// any no-oracle fallback at the spectator surface's own 120/min/IP rate-limit ceiling would drag the
/// full ~200 KiB <c>bytes</c> column through Postgres on EVERY miss — real amplification on a
/// resource-constrained box (a Raspberry Pi), not merely a theoretical one.
/// <para>
/// Mirrors <see cref="PersonaAvatarTokenCache"/>'s own FIXED shape verbatim — the SAME
/// <see cref="CancellationToken.None"/>-inside-the-shared-fetch, per-caller
/// <see cref="Task{TResult}.WaitAsync(CancellationToken)"/>, faulted-entry eviction, and WARN-once
/// idiom that type's own remarks document in full (copied here as the CORRECTED idiom that type
/// shipped AFTER its own PLAN T300 fix round, never the pre-fix shape). The one structural
/// difference: <c>station.station_image</c> is a genuine singleton row (no persona-id key space), so
/// this type memoizes a single <see cref="Task{TResult}"/> slot rather than
/// <see cref="PersonaAvatarTokenCache"/>'s own <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// — the identity-conditional eviction that dictionary's own <c>TryRemove(KeyValuePair)</c> overload
/// provides is reproduced here via <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/> instead,
/// the same "only clear if nobody already installed a fresher replacement" guarantee, expressed for a
/// single mutable field rather than a keyed collection.
/// </para>
/// </summary>
public sealed class StationImageCache(
    IStationImageStore stationImageStore, TimeProvider timeProvider, ILogger<StationImageCache> logger)
{
    /// <summary>How stale a memoized image is allowed to get before the next <see cref="GetAsync"/>
    /// call re-reads the store — the SAME ≤30s bound <see cref="PersonaAvatarTokenCache.StalenessBound"/>
    /// uses, for the identical amplification-control reason (this type's own remarks).</summary>
    public static readonly TimeSpan StalenessBound = TimeSpan.FromSeconds(30);

    // Deliberately NOT `volatile` (PLAN T307 fix round F3): this field is also the target of
    // Interlocked.CompareExchange (the fault-eviction branch below), and passing a `ref` to a
    // volatile field there compiles to CS0420 ("a reference to a volatile field will not be treated
    // as volatile") — under this project's TreatWarningsAsErrors that would be a build break, not
    // merely noise, for a guarantee this field does not need in the first place: every plain
    // read/write here (the cold-fetch install below, and Invalidate()) is a bare object-reference
    // load/store, already atomic on every .NET-supported architecture, and the ONE place ordering
    // could matter — a reader racing a concurrent writer's fresh assignment — is the SAME "still in
    // flight, fall through and race a fresh fetch" case this type's own remarks already accept as
    // harmless/self-correcting (a redundant store read at worst, never a torn or wrong value). The
    // Interlocked.CompareExchange call site is what actually owns the ordering guarantee for the ONE
    // mutation that needs identity-conditional semantics (never clobber a concurrently-installed
    // fresher replacement); every other write here — including Invalidate()'s — is an intentional
    // unconditional last-write-wins, the SAME shape the cold-fetch install already uses, so it stays
    // a plain assignment rather than reaching for Interlocked.Exchange for a guarantee it does not
    // need either.
    Task<Entry>? cached;

    // Warn-once latch (PersonaAvatarTokenCache's own WarnFaultOnce idiom, singular here since there
    // is only ever ONE row to fault on): a store outage logs exactly one WARN for the whole span it
    // stays down, cleared the moment the next fetch succeeds so a LATER, genuinely new outage still
    // gets its own WARN.
    volatile bool warnedFault;

    /// <summary>
    /// Resolves the current station image, or <see langword="null"/> when the owner has never
    /// customized it — served straight from the memo whenever it is younger than
    /// <see cref="StalenessBound"/>, otherwise refreshed with exactly one
    /// <see cref="IStationImageStore.GetAsync"/> call before answering. Never throws: a store fault
    /// degrades to "no customization" for this one call — the same honest fallback answer a station
    /// that never uploaded already gets — mirroring
    /// <see cref="PersonaAvatarTokenCache.GetTokenAsync"/>'s own never-throws contract exactly.
    /// </summary>
    public async Task<StationImage?> GetAsync(CancellationToken ct)
    {
        var current = cached;
        if (current is not null)
        {
            if (current.IsCompletedSuccessfully)
            {
                var warm = await current;
                if (timeProvider.GetUtcNow() - warm.FetchedAt < StalenessBound)
                    return warm.Image;
            }
            else if (current.IsCompleted)
            {
                // Belt-and-braces (PersonaAvatarTokenCache's own F1 fix-round remarks): a memo entry
                // that did NOT complete successfully must never be served. Identity-conditional
                // clear — only replaces THIS exact Task with null, so a concurrent caller's fresh,
                // already-installed replacement is never clobbered — then falls through to a fresh
                // fetch below, same as an ordinary cold miss. Discarded explicitly (`_ =`): the
                // returned previous value is Task<Entry>-shaped, which the compiler would otherwise
                // flag as an unawaited awaitable (CS4014) — this is an Interlocked exchange, not an
                // async call, so there is nothing to await.
                _ = Interlocked.CompareExchange(ref cached, null, current);
            }
            // else: still in flight — fall through and race a fresh fetch, the same harmless,
            // self-correcting inefficiency PersonaAvatarTokenCache's own remarks accept at this
            // boundary.
        }

        var fetch = FetchAsync();
        cached = fetch;

        // WaitAsync(ct): per-CALLER responsiveness only, never the shared fetch's own cancellation —
        // see this type's own remarks. THIS caller stops waiting the instant its own token cancels;
        // the fetch itself keeps running to completion in the background and still memoizes normally
        // for every other caller.
        var entry = await fetch.WaitAsync(ct);
        return entry.Image;
    }

    /// <summary>
    /// Forces the very next <see cref="GetAsync"/> call to re-read the store (PLAN T307 fix round R1)
    /// — <see cref="Api.StationImageController"/> calls this after every successful
    /// <see cref="IStationImageStore.UpsertAsync"/>/<see cref="IStationImageStore.DeleteAsync"/> so the
    /// no-restart contract (SPEC F131.2/F131.3/F131.5) is honest-IMMEDIATE for every reader sharing
    /// this ONE DI-singleton memo — the feeder push path, the spectator fallback ladder, and the admin
    /// console's own <c>GET /api/stations</c> snapshot alike — rather than merely "within
    /// <see cref="StalenessBound"/>". A plain <see langword="null"/> assignment, not
    /// <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>: this write is unconditional by
    /// design, the SAME "last write wins" shape <see cref="GetAsync"/>'s own cold-fetch install above
    /// uses. NOT airtight against every conceivable race — a fetch already in flight when this call
    /// lands, one that began reading the store BEFORE the write this call follows committed, can still
    /// complete afterward and re-populate the memo with the now-stale answer — but this is the exact
    /// same "harmless, self-correcting inefficiency" class this type's own remarks already accept
    /// elsewhere at this boundary: the window is one in-flight fetch wide, not the full
    /// <see cref="StalenessBound"/>, and the very next write's own <see cref="Invalidate"/> call (or
    /// simply the TTL) clears it regardless.
    /// </summary>
    public void Invalidate() => cached = null;

    async Task<Entry> FetchAsync()
    {
        try
        {
            // CancellationToken.None — this fetch is memoized and shared across whichever caller
            // happens to trigger it first, so it must never be bound to any one caller's token (the
            // PersonaAvatarTokenCache F1 fix-round reasoning, applied here verbatim).
            var image = await stationImageStore.GetAsync(CancellationToken.None);
            warnedFault = false;
            return new Entry(image, timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            // Never served warm (mirrors PersonaAvatarTokenCache's own sentinel): FetchedAt ==
            // DateTimeOffset.MinValue guarantees "now - FetchedAt" can never be < StalenessBound, so
            // even if this exact Entry were somehow read back out of the memo it can never look
            // fresh. This call still answers honestly — "no customization" — rather than throwing
            // into any reader's own path; WarnFaultOnce is the operator-facing signal that an
            // outage, not an ordinary never-customized station, is under way.
            WarnFaultOnce(ex);
            return new Entry(null, DateTimeOffset.MinValue);
        }
    }

    void WarnFaultOnce(Exception ex)
    {
        if (warnedFault) return;
        warnedFault = true;

        logger.LogWarning(ex,
            "Failed to resolve the station image — degrading to the shipped fallback until the store recovers");
    }

    sealed record Entry(StationImage? Image, DateTimeOffset FetchedAt);
}
