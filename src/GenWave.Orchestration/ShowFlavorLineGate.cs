using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F116.3 (STORY-308, PLAN T249) — the show-flavor patter line's own cadence gate: an ordinary
/// LeadIn/BackAnnounce break during a show may carry the show's flavor as spoken color, at most once
/// per <see cref="IShowPatterCadenceProvider.PatterCadenceMinutes"/> window PER SHOW (0 = off, the
/// fail-closed default — <c>Station:Shows:PatterCadenceMinutes</c>). Mirrors
/// <c>GenWave.Context.ContextPipeline</c>'s own placement one project over (SPEC F107.5's own
/// precedent): the interface this implements (<see cref="IShowFlavorLineSource"/>) lives in
/// <c>GenWave.Core.Abstractions</c> so <c>GenWave.Tts.LlmCopyWriter</c> can depend on the CONTRACT
/// without a project reference to this L1 project — exactly the same seam shape
/// <see cref="IContextPatterFactSource"/> already established one seam over. Show identity itself is
/// Orchestration's own domain (<see cref="OnAirPersonaAccessor"/>, <see cref="CachingScheduleResolver"/>,
/// <see cref="HandoffContext"/> already live here), so this gate lives beside them rather than in a
/// project that has never otherwise touched a show.
///
/// <para>
/// <b>Gate state, in-memory, per show (F116.3's own wording) — the weather-freshness precedent.</b> A
/// plain in-memory map of show id → last-spoken instant, never persisted, so a process restart reopens
/// every show's gate immediately — the same "restart forgets, and that is fine" posture
/// <c>ContextPipeline</c>'s own per-provider cadence state carries. Reads <see cref="scheduleResolver"/>'s
/// cached <c>OnAirSnapshot.Show</c> (<see cref="CachingScheduleResolver.TryGetCurrent"/> — no store
/// round trip) rather than a second resolve, mirroring <see cref="OnAirPersonaAccessor.ActiveShowId"/>'s
/// own read one member over.
/// </para>
///
/// <para>
/// <b>A consuming read, exactly like <see cref="IContextPatterFactSource.TryTakeDuePatterFact"/>:</b> a
/// non-null return stamps THIS instant as the show's last-spoken time, so a second call before the next
/// cadence window elapses answers null for that show. <c>GenWave.Tts.LlmCopyWriter</c> is the one and
/// only caller (SPEC F116.3's own arbitration: "context wins... the show gate stays open for the next
/// eligible break") — it calls this ONLY when no context fact already claimed the slot; simply never
/// calling this method at all is what keeps a lost slot from ever spending the show's own cadence
/// window (no separate "peek" mode needed — see <c>LlmCopyWriter.TakeDueShowFlavorLineForOnAirRender</c>'s
/// own remarks).
/// </para>
///
/// <para>
/// <b>Thread safety.</b> <c>Orchestrator.EnqueuePatterAsync</c> starts a unit's BackAnnounce and LeadIn
/// renders concurrently, and <c>LlmCopyWriter.WriteAsync</c> may call this method from either before
/// either completes — so the check-then-stamp below is guarded by <see cref="gate"/>, mirroring
/// <c>ContextPipeline.ProviderState</c>'s own per-instance lock one project over.
/// </para>
///
/// <para>
/// <b><see cref="lastSpokenByShowId"/> never evicts an entry</b> — it is bounded by the number of
/// DISTINCT show ids this process has ever seen on the air since it started, a small, station-owned
/// number (tens at most, per <see cref="IShowStore"/>'s own CRUD scale), so the unbounded-by-name
/// growth is negligible over any real process lifetime; a restart clears it entirely regardless (see
/// this class's own "gate state, in-memory" remarks above).
/// </para>
/// </summary>
public sealed class ShowFlavorLineGate(
    CachingScheduleResolver scheduleResolver, IShowPatterCadenceProvider cadenceProvider, TimeProvider timeProvider)
    : IShowFlavorLineSource
{
    readonly object gate = new();
    readonly Dictionary<long, DateTimeOffset> lastSpokenByShowId = [];

    /// <inheritdoc/>
    public ShowFlavorFact? TryTakeDueShowLine()
    {
        var cadenceMinutes = cadenceProvider.PatterCadenceMinutes;
        if (cadenceMinutes <= 0)
            return null; // Off (the fail-closed default) — Station:Shows:PatterCadenceMinutes unset/0.

        if (scheduleResolver.TryGetCurrent()?.Show is not { } show)
            return null; // Showless station, or before the schedule has ever resolved (boot window).

        if (string.IsNullOrWhiteSpace(show.Flavor))
            return null; // Nothing to say — never stamps the gate for a show with no flavor text.

        var now = timeProvider.GetUtcNow();
        lock (gate)
        {
            if (lastSpokenByShowId.TryGetValue(show.Id, out var lastSpoken)
                && now - lastSpoken < TimeSpan.FromMinutes(cadenceMinutes))
            {
                return null; // Not due yet for THIS show — a different show's own window is independent.
            }

            lastSpokenByShowId[show.Id] = now;
        }

        return new ShowFlavorFact(show.Name, show.Flavor);
    }
}
