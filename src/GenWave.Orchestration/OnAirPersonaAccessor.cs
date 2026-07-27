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
/// </summary>
public sealed class OnAirPersonaAccessor(
    CachingScheduleResolver scheduleResolver, IPersonaStore personaStore, ILogger<OnAirPersonaAccessor> logger)
    : IActivePersonaAccessor
{
    long lastWarnedPersonaId;
    long lastWarnedCardPersonaId;

    // Not a WarnOnce-by-id dedup (a schedule-load fault names no persona id to key off) — a simple
    // "already warned for this outage episode" latch instead, cleared the moment a resolve succeeds
    // again so a LATER, genuinely new outage still gets its own WARN.
    volatile bool scheduleFaultWarned;

    /// <inheritdoc/>
    public async Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        if (await TryResolveOnAirAsync(ct) is not { PersonaId: { } personaId })
            return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(personaId, ct);
            if (persona is null)
            {
                WarnOnce(ref lastWarnedPersonaId, personaId, () => logger.LogWarning(
                    "On-air segment names persona id={PersonaId} with no matching persona row — " +
                    "degrading to persona-less", personaId));
            }
            return persona;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WarnOnce(ref lastWarnedPersonaId, personaId, () => logger.LogWarning(ex,
                "Failed to resolve on-air persona id={PersonaId} — degrading to persona-less", personaId));
            return null;
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
