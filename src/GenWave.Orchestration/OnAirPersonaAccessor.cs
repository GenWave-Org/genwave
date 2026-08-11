using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The resolver-backed <see cref="IActivePersonaAccessor"/> (SPEC F91.5, STORY-241/242, PLAN T120):
/// replaces the retired <c>Station:Persona:ActiveId</c>-reading accessor with the schedule grid's own
/// on-air answer. <see cref="ResolveAsync"/>/<see cref="ResolveCardAsync"/> resolve
/// <paramref name="scheduleResolver"/>'s <see cref="OnAirSnapshot.PersonaId"/> and look the row up
/// through the SAME <paramref name="personaStore"/> the retired accessor read, so every one of this
/// seam's existing consumers (Orchestrator, ranker rung 0, the LLM copywriter, the corrections cache,
/// the booth log, the persona-card migrator, the status endpoint, the persona controller) observes
/// the on-air persona with ZERO call-site changes (F91.5).
///
/// <para>
/// Same never-throws, same WarnOnce-per-stale-id degrade contract as the retired accessor (F35.5): a
/// grid gap (<see cref="OnAirSnapshot.PersonaId"/> null) is the default "no persona" state — no log;
/// a schedule row naming a persona that no longer exists (deleted out of band — F91.9's FK guard is
/// PLAN T121's own later addition, not a guarantee this type can lean on) degrades to persona-less
/// with one WARN per distinct stale id, mirroring the retired accessor's own
/// <c>lastWarnedActiveId</c>/<c>lastWarnedCardActiveId</c> dedup fields.
/// </para>
///
/// <para>
/// <b>The retired accessor's OWN never-throws contract only ever had to defend one I/O call
/// (<see cref="IPersonaStore.GetByIdAsync"/>/<see cref="IPersonaStore.GetCardByIdAsync"/>) — reading
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c> first was pure, in-memory, never faulting, and the
/// common "no active persona" (<c>ActiveId&lt;=0</c>) case never touched <see cref="IPersonaStore"/>
/// at all, preserving the documented "a deployment with no Station Postgres configured at all is a
/// legal, working configuration" contract (<c>PersonaRepository</c>'s own remarks). This type's own
/// FIRST read — <see cref="CachingScheduleResolver.ResolveAsync"/> — is now ALSO real I/O
/// (<c>IScheduleStore.LoadWeekAsync</c>), so preserving that same "no Station Postgres = a supported,
/// no-persona-ever configuration" behavior requires wrapping THAT call in the identical
/// never-throws/degrade discipline, not only the persona-store lookup below it — see
/// <see cref="TryResolveOnAirAsync"/>.</b>
/// </para>
///
/// <para>
/// <see cref="ActivePersonaId"/> — the synchronous hot-path member <c>BoothLogWriter</c> stamps at
/// air time — reads <see cref="CachingScheduleResolver.TryGetCurrent"/> instead of an
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c> snapshot: no store round trip, no awaiting, same
/// contract (and no I/O to ever fault on, unlike <see cref="TryResolveOnAirAsync"/>). Before the very
/// first <see cref="CachingScheduleResolver.ResolveAsync"/> completes (the process boot window, or a
/// deployment whose schedule store never resolves at all) this answers <see langword="null"/> —
/// exactly the same "no persona yet" shape <c>BoothLogWriter</c> already tolerates for a genuine gap,
/// so a track airing before the schedule has ever resolved simply stamps no persona rather than
/// faulting.
/// </para>
///
/// <para>
/// <b><see cref="TryGetCachedName"/> — the DB-free display-name memo (SPEC F93.1/F93.4, STORY-244,
/// PLAN T125):</b> the spectator now-playing poll needs the on-air persona's display NAME, but
/// F93.4 forbids it a DB call on the poll path, and <see cref="ActivePersonaId"/> only ever answers
/// an id. Rather than add a dedicated poller (more moving parts, and still eventually a DB read),
/// this type piggybacks on I/O it ALREADY performs: every successful <see cref="ResolveAsync"/> —
/// reached, in the shipped default configuration, once per unit via <c>Orchestrator</c>'s lead-in/
/// back-announce persona resolution, and independently via <c>RankerPersonaPickProvider.TryPickAsync</c>
/// when the ranker is bound — stamps the resolved <see cref="Persona.Name"/> into an in-memory
/// <c>ConcurrentDictionary</c> keyed by id. <see cref="TryGetCachedName"/> only ever reads that
/// dictionary; it never triggers a resolve itself.
/// <para>
/// Staleness bound: an admin rename reaches the cache on that persona's next natural
/// <see cref="ResolveAsync"/> (about one unit later in the shipped configuration) — the same class
/// of one-ahead trade-off F92.6 already accepts for handoff ceremony timing, not a new risk this
/// memo introduces. A deployment whose cadence disables BOTH lead-in and back-announce AND runs the
/// no-op pick provider (neither the shipped default) never calls <see cref="ResolveAsync"/> at all,
/// so the memo never populates and the spectator surface reports <c>dj: null</c> even though a
/// persona is scheduled — an honest "unknown", never a stall or a stale guess.
/// </para>
/// <para>
/// <b>The NEXT persona is warmed too (PLAN T125 review F1 — a real defect, not a docs gap):</b>
/// without this, <c>SpectatorController.ResolveUpNext</c>'s <c>upNext.dj</c> would report
/// <see langword="null"/> ("Nonstop music" to listeners) for ANY persona that has never yet been
/// CURRENT since process start — the incoming DJ of a schedule boundary is, by definition, exactly
/// that persona, every single time, until the moment they actually go on air. Worst case: a gap
/// rolls into a staffed segment, and <see cref="ResolveAsync"/> used to short-circuit to
/// <see langword="null"/> on a null <see cref="OnAirSnapshot.PersonaId"/> before ever looking at
/// <see cref="OnAirSnapshot.NextSegment"/> — misreporting the incoming DJ's name for their entire
/// first segment. <see cref="ResolveAsync"/> now ALSO resolves
/// <see cref="OnAirSnapshot.NextSegment"/>'s own <c>PersonaId</c> off the SAME snapshot it already
/// holds, whenever it names someone other than the current persona (a same-persona boundary already
/// gets its name from the current-persona branch, or will on its own next natural tick — re-reading
/// it here would just be a redundant duplicate of the SAME lookup). This is deliberately
/// UNCONDITIONAL, not gated on "not already memoized": the incremental cost is one extra
/// <see cref="IPersonaStore.GetByIdAsync"/> call per unit — the exact same bounded-cost class as the
/// current-persona read right above it, paid once per <c>Orchestrator</c>/ranker resolve, never on
/// the spectator poll path (F93.4 unaffected either way) — and reading it unconditionally also
/// refreshes an admin rename of the NEXT persona within a single unit, rather than only after that
/// persona's own segment starts airing.
/// </para>
/// </para>
/// </summary>
public sealed class OnAirPersonaAccessor(
    CachingScheduleResolver scheduleResolver, IPersonaStore personaStore, ILogger<OnAirPersonaAccessor> logger)
    : IActivePersonaAccessor
{
    readonly ConcurrentDictionary<long, string> cachedNames = new();

    long lastWarnedPersonaId;
    long lastWarnedCardPersonaId;

    // Its own dedup key (PLAN T125 review F1) — deliberately never shared with lastWarnedPersonaId:
    // sharing one field between the current-persona and next-persona fetches would let the OTHER
    // one's warn overwrite this latch every unit, defeating the "one warn per stale id" contract for
    // BOTH the moment they happen to alternate (exactly the class of interference lastWarnedCardPersonaId's
    // own remarks already call out for ResolveCardAsync).
    long lastWarnedNextPersonaId;

    // Not a WarnOnce-by-id dedup (a schedule-load fault names no persona id to key off) — a simple
    // "already warned for this outage episode" latch instead, cleared the moment a resolve succeeds
    // again so a LATER, genuinely new outage still gets its own WARN.
    volatile bool scheduleFaultWarned;

    /// <inheritdoc/>
    public async Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        var onAir = await TryResolveOnAirAsync(ct);
        if (onAir is null)
            return null;

        Persona? current = null;
        if (onAir.PersonaId is { } personaId)
        {
            var (persona, fault) = await FetchPersonaAsync(personaId, ct);
            current = persona;
            if (fault is not null)
            {
                WarnOnce(ref lastWarnedPersonaId, personaId, () => logger.LogWarning(fault,
                    "Failed to resolve on-air persona id={PersonaId} — degrading to persona-less", personaId));
            }
            else if (persona is null)
            {
                WarnOnce(ref lastWarnedPersonaId, personaId, () => logger.LogWarning(
                    "On-air segment names persona id={PersonaId} with no matching persona row — " +
                    "degrading to persona-less", personaId));
            }
        }

        // Warm the NEXT persona's display name off the SAME snapshot (PLAN T125 review F1 — see this
        // type's own remarks for the full rationale/cost bound). Skipped when it names the SAME
        // persona as current: that id already got its name (or WarnOnce) from the branch above.
        if (onAir.NextSegment?.PersonaId is { } nextPersonaId && nextPersonaId != onAir.PersonaId)
        {
            var (_, nextFault) = await FetchPersonaAsync(nextPersonaId, ct);
            if (nextFault is not null)
            {
                WarnOnce(ref lastWarnedNextPersonaId, nextPersonaId, () => logger.LogWarning(nextFault,
                    "Failed to resolve upcoming persona id={PersonaId} — degrading to no cached name",
                    nextPersonaId));
            }
            // A next-persona id with no matching row degrades SILENTLY here (no WARN, unlike the
            // current-persona branch above): the same stale id gets its one WarnOnce from that very
            // branch the moment it actually becomes current — logging it again here, one unit early,
            // would just double the line for the same event.
        }

        return current;
    }

    /// <summary>
    /// Fetches <paramref name="personaId"/> and, on success, stamps its name into the DB-free
    /// display-name memo (SPEC F93.1/F93.4) — shared by <see cref="ResolveAsync"/>'s current- and
    /// next-persona reads so both warm the SAME memo the SAME way. Never throws (F35.5): a
    /// cancellation propagates, everything else comes back as <c>(null, ex)</c> for the caller's own
    /// WarnOnce dedup (each call site keys its own warn on its OWN id, so this method never decides
    /// which dedup field applies).
    /// </summary>
    async Task<(Persona? Persona, Exception? Fault)> FetchPersonaAsync(long personaId, CancellationToken ct)
    {
        try
        {
            var persona = await personaStore.GetByIdAsync(personaId, ct);
            if (persona is not null)
                cachedNames[personaId] = persona.Name;
            return (persona, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <inheritdoc/>
    public async Task<PersonaCard?> ResolveCardAsync(CancellationToken ct)
    {
        if (await TryResolveOnAirAsync(ct) is not { PersonaId: { } personaId })
            return null;

        try
        {
            // A missing/card-less row degrades silently here — NO log: ResolveAsync's own call for
            // this same persona id already reports a stale id once (its own WarnOnce); logging it
            // again from this sibling method would just double the line for one event.
            return await personaStore.GetCardByIdAsync(personaId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WarnOnce(ref lastWarnedCardPersonaId, personaId, () => logger.LogWarning(ex,
                "Failed to resolve on-air persona card id={PersonaId} — degrading to no card", personaId));
            return null;
        }
    }

    /// <inheritdoc/>
    public long? ActivePersonaId => scheduleResolver.TryGetCurrent()?.PersonaId;

    /// <inheritdoc/>
    public string? TryGetCachedName(long personaId) =>
        cachedNames.TryGetValue(personaId, out var name) ? name : null;

    /// <summary>
    /// SPEC F121.1 (STORY-310, PLAN T242) — reads the SAME cached snapshot <see cref="ActivePersonaId"/>
    /// does, off <see cref="OnAirSnapshot.Show"/> instead of <see cref="OnAirSnapshot.PersonaId"/>: no
    /// second resolve, no new I/O, the identical "before the first resolve, or an empty grid, answers
    /// null" boot-window behavior.
    /// </summary>
    public long? ActiveShowId => scheduleResolver.TryGetCurrent()?.Show?.Id;

    /// <summary>
    /// Resolves the on-air snapshot, degrading to <see langword="null"/> on any
    /// <see cref="CachingScheduleResolver.ResolveAsync"/> fault (F12.4) — most notably an unconfigured
    /// <c>ConnectionStrings:Station</c> (empty string), the documented "no Station Postgres at all"
    /// deployment shape the retired accessor's own <c>ActiveId&lt;=0</c> short-circuit preserved by
    /// construction. A caller seeing <see langword="null"/> here cannot distinguish "grid gap" from
    /// "schedule store faulted" — both degrade identically to persona-less, which is exactly right:
    /// neither is an error this seam's callers should ever see surfaced as one (F35.5).
    /// </summary>
    async Task<OnAirSnapshot?> TryResolveOnAirAsync(CancellationToken ct)
    {
        try
        {
            var onAir = await scheduleResolver.ResolveAsync(ct);
            scheduleFaultWarned = false;
            return onAir;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!scheduleFaultWarned)
            {
                scheduleFaultWarned = true;
                logger.LogWarning(ex,
                    "Failed to resolve the on-air schedule — degrading to persona-less until it recovers");
            }
            return null;
        }
    }

    static void WarnOnce(ref long lastWarned, long personaId, Action logAction)
    {
        if (Interlocked.Exchange(ref lastWarned, personaId) == personaId)
            return;

        logAction();
    }
}
