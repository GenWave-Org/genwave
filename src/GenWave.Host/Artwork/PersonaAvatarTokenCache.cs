using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Artwork;

/// <summary>
/// SPEC F129.5 (STORY-336, PLAN T300) — persona id → worn-face token, memoized on a ≤30s TTL so
/// the feeder push path (<see cref="Engine.ArtworkUrlResolver"/>) issues zero per-tick
/// <see cref="IPersonaAvatarStore"/> reads, following the "durationMs-rehydrator idiom"
/// <see cref="Playout.DurationRehydrator"/> already ships: a <see cref="Task{TResult}"/>-valued
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> memo so a warm read never re-awaits the store at
/// all, and a failed fetch memoizes a permanently-stale sentinel — never removed, but never able to
/// evaluate as fresh — so the very next call always re-fetches once the store recovers, rather than
/// wedging a false "no face" in place.
/// <para>
/// The one addition <see cref="DurationRehydrator"/>'s own memo does not need: a bounded staleness
/// window. A track's duration never changes once measured; a worn face DOES — upload, apply-from-
/// pack, and remove (PLAN T295) all rotate <see cref="IPersonaAvatarStore.GetTokenByPersonaIdAsync"/>'s
/// answer for that persona id — so an entry older than <see cref="StalenessBound"/> is refetched
/// rather than served forever.
/// </para>
/// <para>
/// <b>THE ONE SHARED MEMO (gh-#482 rider, PLAN T300-review rider 2):</b> this is deliberately the
/// ONLY persona-id→token cache in the codebase, a DI singleton read by BOTH
/// <see cref="Engine.ArtworkUrlResolver"/> (the ICY stream's <c>url=</c> annotation) and
/// <see cref="Api.SpectatorController"/> (the now-playing payload's <c>djAvatarUrl</c>) — never two
/// independently-cached copies that could answer a stale-vs-fresh token differently for the same
/// instant, and never a fourth near-duplicate of the three existing 30s-TTL
/// <c>ActivePersonaXxxCache</c> types in <c>GenWave.Tts</c> (gh-#482's own "rule of three" — this
/// type is keyed by an explicit <c>personaId</c> parameter rather than "whichever persona is
/// currently active", a genuinely different shape those three do not fit, so it follows
/// <see cref="DurationRehydrator"/>'s idiom instead of duplicating theirs).
/// </para>
/// <para>
/// <b>THE SHARED FETCH OWNS NO CALLER'S CANCELLATION (PLAN T300 fix round F1 — a proven
/// broadcast-killer).</b> <see cref="FetchAsync"/> runs the store read on
/// <see cref="CancellationToken.None"/>, never a caller-supplied token: a memoized fetch is shared
/// across whichever caller happens to trigger it first, so it "has no single owning request or tick
/// to bind a token to, and it must complete on its own regardless of the caller that started it" —
/// <see cref="DurationRehydrator.FetchAndMemoizeAsync"/>'s own comment, verbatim, for the identical
/// shape. Before this fix, a cancelled caller's token reached the store call directly: a spectator
/// request aborting mid-fetch left a permanently-CANCELLED <see cref="Task{TResult}"/> memoized —
/// every later feeder push re-awaiting that same entry threw immediately, wedging the whole
/// broadcast until a restart. Per-caller responsiveness, when a caller wants it, lives at the AWAIT
/// site instead (<see cref="GetTokenAsync"/>'s own <c>WaitAsync(ct)</c>): that caller stops waiting
/// the moment its own token cancels, while the shared fetch keeps running and still memoizes
/// normally for every other caller.
/// </para>
/// <para>
/// <b>ROSTER-BOUNDED, NOT REQUEST-BOUNDED (PLAN T300 fix round F7).</b> Every key this memo ever
/// sees is a persona id sourced from <see cref="Core.Abstractions.IActivePersonaAccessor.ActivePersonaId"/>
/// — itself the schedule resolver's on-air answer, never a request-supplied value — so the key
/// space is bounded by the size of the persona roster, not by request or listener volume; unlike
/// <c>ArtworkService.knownArtless</c> (keyed by an attacker-mintable token), no hostile caller can
/// grow this dictionary. <see cref="MaxMemoEntries"/> is still a defensive full-clear ceiling in
/// <see cref="DurationRehydrator"/>'s own spirit — cheap insurance against a roster that somehow
/// grows unexpectedly large, not a load-bearing guard against an actually-unbounded key space.
/// </para>
/// </summary>
public sealed class PersonaAvatarTokenCache(
    IPersonaAvatarStore avatarStore, TimeProvider timeProvider, ILogger<PersonaAvatarTokenCache> logger)
{
    /// <summary>How stale a memoized token is allowed to get before the next
    /// <see cref="GetTokenAsync"/> call re-reads the store — SPEC F129.5's own bound.</summary>
    public static readonly TimeSpan StalenessBound = TimeSpan.FromSeconds(30);

    /// <summary>Defensive full-clear ceiling (PLAN T300 fix round F7) — see this type's own
    /// ROSTER-BOUNDED, NOT REQUEST-BOUNDED remarks. <see cref="DurationRehydrator"/>'s own bound,
    /// reused verbatim: this memo's realistic key count (one persona roster) sits nowhere near it.</summary>
    const int MaxMemoEntries = 512;

    readonly ConcurrentDictionary<long, Task<Entry>> memoByPersonaId = new();

    // Warn-once-per-persona latch (OnAirPersonaAccessor's own WarnOnce-per-stale-id idiom, PLAN
    // T300 fix round F2): a store outage logs exactly one WARN per persona id for the whole span it
    // stays down, not one per tick. Cleared the moment that persona's next fetch succeeds, so a
    // LATER, genuinely new outage still gets its own WARN — mirrors OnAirPersonaAccessor's own
    // scheduleFaultWarned reset.
    readonly ConcurrentDictionary<long, byte> warnedFaultPersonaIds = new();

    /// <summary>
    /// Resolves <paramref name="personaId"/>'s current worn-face token, or <see langword="null"/>
    /// when that persona wears none — served straight from the memo whenever it is younger than
    /// <see cref="StalenessBound"/>, otherwise refreshed with exactly one
    /// <see cref="IPersonaAvatarStore.GetTokenByPersonaIdAsync"/> call before answering. Never
    /// throws: a store fault degrades to "no token" for this one call (the same honest "no face"
    /// answer an unfaced persona already gets). The fault itself is memoized only as a
    /// permanently-stale sentinel that never evaluates as fresh, so the very next call still
    /// retries once the store recovers. A still-good-but-expired token is discarded, not served,
    /// when its refresh faults — the fresh fault outranks the stale-but-good answer; broadcast
    /// stays never-silent because the caller falls to the station image, not dead air (deliberate).
    /// </summary>
    public async Task<string?> GetTokenAsync(long personaId, CancellationToken ct)
    {
        if (memoByPersonaId.TryGetValue(personaId, out var cached))
        {
            if (cached.IsCompletedSuccessfully)
            {
                var warm = await cached;
                if (timeProvider.GetUtcNow() - warm.FetchedAt < StalenessBound)
                    return warm.Token;
            }
            else if (cached.IsCompleted)
            {
                // Belt-and-braces (PLAN T300 fix round F1). FetchAsync's own never-throws contract
                // means this branch should be unreachable in practice, but a memo entry that did NOT
                // complete successfully — faulted or cancelled, by any cause — must never be served:
                // evict it (identity-conditional TryRemove — only removes THIS exact Task, so a
                // concurrent caller's fresh, already-installed replacement for the same key is never
                // clobbered, PLAN T300 fix round F6) and fall through to a fresh fetch below, same
                // as an ordinary cold miss. No faulted future is ever served twice.
                memoByPersonaId.TryRemove(new KeyValuePair<long, Task<Entry>>(personaId, cached));
            }
            // else: still in flight — fall through and race a fresh fetch (see the comment on the
            // fetch below: a harmless, self-correcting inefficiency at this boundary, not a
            // correctness risk).
        }

        // Cold, or stale: (re)fetch. Concurrent callers racing the SAME cold/stale discovery each
        // issue their own fetch and simply overwrite one another in the memo — a harmless,
        // self-correcting inefficiency at a 30s boundary, not a correctness risk; every WARM read
        // above this line still shares one memoized Task with no redundant store call at all.
        if (memoByPersonaId.Count > MaxMemoEntries) memoByPersonaId.Clear();
        var fetch = FetchAsync(personaId);
        memoByPersonaId[personaId] = fetch;

        // WaitAsync(ct): per-CALLER responsiveness only, never the shared fetch's own cancellation
        // (see this type's own remarks above — PLAN T300 fix round F1). THIS caller stops waiting
        // the instant its own token cancels; the fetch itself keeps running to completion in the
        // background and still memoizes normally for every other caller.
        var entry = await fetch.WaitAsync(ct);
        return entry.Token;
    }

    async Task<Entry> FetchAsync(long personaId)
    {
        try
        {
            // CancellationToken.None — see this type's own THE SHARED FETCH OWNS NO CALLER'S
            // CANCELLATION remarks (PLAN T300 fix round F1): this fetch is memoized and shared, so
            // it must never be bound to any one caller's token.
            var token = await avatarStore.GetTokenByPersonaIdAsync(personaId, CancellationToken.None);
            warnedFaultPersonaIds.TryRemove(personaId, out _);
            return new Entry(token, timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            // Never served warm (mirrors DurationRehydrator's own contract): FetchedAt ==
            // DateTimeOffset.MinValue guarantees "now - FetchedAt" can never be < StalenessBound, so
            // even if this exact Entry were somehow read back out of the memo it can never look
            // fresh — that is the sentinel this Entry actually carries. GetTokenAsync's own
            // IsCompletedSuccessfully/eviction belt-and-braces (F6, above) is the independent
            // guarantee that a faulted Task itself is never served twice; this sentinel is the
            // second, complementary guarantee for the ordinary case where FetchAsync answers
            // successfully with "no answer available right now". This call still answers honestly —
            // "no token" — rather than throwing into the push/payload path (F129.5); WarnFaultOnce
            // is the operator-facing signal that an outage, not an ordinary unfaced persona, is
            // under way.
            WarnFaultOnce(personaId, ex);
            return new Entry(null, DateTimeOffset.MinValue);
        }
    }

    void WarnFaultOnce(long personaId, Exception ex)
    {
        if (!warnedFaultPersonaIds.TryAdd(personaId, 0)) return;

        logger.LogWarning(ex,
            "Failed to resolve worn-face token for persona id={PersonaId} — degrading to no face " +
            "until the store recovers", personaId);
    }

    sealed record Entry(string? Token, DateTimeOffset FetchedAt);
}
