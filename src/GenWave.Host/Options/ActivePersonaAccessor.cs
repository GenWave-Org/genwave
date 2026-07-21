using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side implementation of <see cref="IActivePersonaAccessor"/> (SPEC F35.2, F35.5):
/// resolves <c>Station:Persona:ActiveId</c> through <see cref="IOptionsMonitor{StationOptions}"/>
/// and the row through <see cref="IPersonaStore"/> — both re-read fresh on every call (mirrors
/// <see cref="OptionsMonitorStationScopeProvider"/>) so a live activate/deactivate (an
/// <c>Api.PersonaController</c> write through the F19 overlay) takes effect on the very next
/// render with no api restart. Nothing is cached here beyond what <see cref="IOptionsMonitor{T}"/>
/// already caches.
/// </summary>
/// <remarks>
/// A write to <c>Station:Persona:ActiveId</c> — including <c>Api.PersonaController</c>'s
/// delete-clears-active write (F35.5) — is what this accessor's next call observes.
/// </remarks>
sealed class ActivePersonaAccessor(
    IOptionsMonitor<StationOptions> stationMonitor,
    IPersonaStore personaStore,
    ILogger<ActivePersonaAccessor> logger) : IActivePersonaAccessor
{
    // T6 reviewer follow-up (T4): ResolveAsync is called once per TTS render, forever, at cadence
    // scale — an ActiveId that goes stale (missing row, or a persistent store fault) would otherwise
    // WARN on every single one of those calls. Rate-limited to "once per distinct stale id/failure":
    // the field remembers the last ActiveId a WARN was already emitted for, so the same stale value
    // logs exactly once and stays quiet until the id changes (e.g. the operator activates a
    // different persona, or fixes the overlay). Deliberately simple over a time-window scheme — a
    // process restart or an id change both naturally re-arm it.
    long lastWarnedActiveId;

    // Separate from lastWarnedActiveId (T37, STORY-193): ResolveAsync and ResolveCardAsync are two
    // independent reads of the SAME activeId, each with its own WarnOnce dedup — sharing one field
    // would let one method's warn silently suppress the other's for a genuinely distinct failure.
    long lastWarnedCardActiveId;

    public async Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        var activeId = stationMonitor.CurrentValue.Persona.ActiveId;

        // 0/absent is the default "no persona" state, not a degradation — no log.
        if (activeId <= 0)
            return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(activeId, ct);
            if (persona is null)
            {
                // F35.5: a stale ActiveId (the persona was deleted through some path other than
                // the controller's delete-clears-active write, or the overlay was hand-edited)
                // degrades to persona-less rather than failing the render.
                WarnOnce(activeId, () => logger.LogWarning(
                    "Station:Persona:ActiveId={ActiveId} has no matching persona row — degrading to persona-less",
                    activeId));
            }
            return persona;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // F12.4 discipline extended to personas: a store failure degrades the same way a
            // missing row does — the render path always gets an answer, never a stall.
            WarnOnce(activeId, () => logger.LogWarning(ex,
                "Failed to resolve active persona id={ActiveId} — degrading to persona-less", activeId));
            return null;
        }
    }

    void WarnOnce(long activeId, Action logAction)
    {
        if (Interlocked.Exchange(ref lastWarnedActiveId, activeId) == activeId)
            return;

        logAction();
    }

    /// <summary>
    /// SPEC F71.1/F71.3/F71.7 (STORY-193): the card-definition counterpart to <see cref="ResolveAsync"/>,
    /// re-reading <c>Station:Persona:ActiveId</c> fresh exactly the same way.
    /// </summary>
    public async Task<PersonaCard?> ResolveCardAsync(CancellationToken ct)
    {
        var activeId = stationMonitor.CurrentValue.Persona.ActiveId;

        if (activeId <= 0)
            return null;

        try
        {
            // A missing/card-less row degrades silently here — NO log: ResolveAsync's own call for
            // this same activeId already reports a stale ActiveId once (its own WarnOnce); logging
            // it again from this sibling method would just double the line for one event.
            return await personaStore.GetCardByIdAsync(activeId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unlike a missing row, an unexpected store fault IS worth its own line — ResolveAsync's
            // own catch block only covers ITS OWN GetByIdAsync call, not this one.
            WarnCardOnce(activeId, () => logger.LogWarning(ex,
                "Failed to resolve active persona card id={ActiveId} — degrading to no card", activeId));
            return null;
        }
    }

    void WarnCardOnce(long activeId, Action logAction)
    {
        if (Interlocked.Exchange(ref lastWarnedCardActiveId, activeId) == activeId)
            return;

        logAction();
    }

    /// <summary>
    /// SPEC F84.6, STORY-215: a pure <see cref="IOptionsMonitor{StationOptions}.CurrentValue"/> read —
    /// no persona-store round trip, no WARN, no caching beyond what <see cref="IOptionsMonitor{T}"/>
    /// already does. <see cref="BoothLogWriter"/> calls this synchronously on its hot path to stamp
    /// the persona on air AT THE MOMENT a track starts, before the row ever reaches the drain queue.
    /// Unlike <see cref="ResolveAsync"/> this never validates the id against <c>IPersonaStore</c> — a
    /// stale/dangling id (the persona deleted moments later) is a booth-log append-time concern
    /// (<c>BoothLogRepository.AppendAsync</c>'s own FK-violation degrade), not this accessor's.
    /// </summary>
    public long? ActivePersonaId
    {
        get
        {
            var activeId = stationMonitor.CurrentValue.Persona.ActiveId;
            return activeId <= 0 ? null : activeId;
        }
    }
}
