using GenWave.Core.Abstractions;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F110.1/F110.3 (STORY-301/302, PLAN T230) — the top-of-hour producer for clock-anchored
/// idents and time announcements: settings-gated (<c>Station:Imaging:ClockAnchoredIdents</c>/
/// <c>Station:Imaging:TimeAnnouncements</c>), read fresh on every call through
/// <see cref="IStationImagingSettingsProvider"/>. One idempotent public method,
/// <see cref="Produce"/>, that the Host's <c>ContextTickerService</c> calls each tick — this class
/// owns no timer or loop of its own (mirrors <see cref="Orchestrator"/>'s own handoff-producer split:
/// the TRIGGER lives here/there, the DRAIN stays the Orchestrator's).
///
/// <para>
/// <b>Due-ness (F110.1/F110.3):</b> every call that finds at least one knob on enqueues a
/// future-dated deferral due at the next station-local top-of-hour — never "now" — but ONLY when
/// nothing of that kind is already pending
/// (<see cref="SpeechDeferralQueue.EnqueueIfAbsent"/>, PLAN T230 review F1). Repeated ticks before the
/// hour arrives see the same pending entry every time and no-op, which collapses to exactly one
/// pending deferral per kind exactly as a plain supersede would — but a plain supersede
/// (<see cref="SpeechDeferralQueue.Enqueue"/>'s own <c>(kind, null discriminator)</c> contract, SPEC
/// F74.2) is the WRONG tool for the tick immediately AFTER the hour turns: that tick recomputes a
/// NEW, LATER due instant (the FOLLOWING hour), and a supersede would silently discard the
/// still-pending, not-yet-drained deferral for the hour that JUST started — a real defect (PLAN T230
/// review F1): a boundary drain can land anywhere up to one ticker interval after the hour turns, and
/// the deferral must survive that whole window. <see cref="SpeechDeferralQueue.EnqueueIfAbsent"/>
/// never displaces a pending entry, drained or not, so it always does. The instant the pending
/// deferral actually drains, the queue is empty again and the very next tick arms the FOLLOWING hour —
/// normal flow resumes. The same protection covers a deferral pending for HOURS (station idle
/// overnight, no boundary decision made): every intervening tick keeps seeing it pending and no-ops,
/// so exactly ONE deferral airs at the next boundary (SPEC F74.1), never a backlog of missed hours.
/// </para>
///
/// <para>
/// <b>F74.3 arming note:</b> a future-dated deferral sitting in <see cref="SpeechDeferralQueue"/>
/// arms boundary-aware track selection toward it (<see cref="SpeechDeferralQueue.NextDue"/>'s own
/// remarks, PLAN T43) — no code here does that biasing; enqueuing the deferral is enough. Alongside
/// <see cref="Orchestrator"/>'s own handoff producer, this is one of the future-dated producers the
/// F74.3 revisit contemplated ahead of time: a track ending near the coming hour is now preferred
/// the same way one ending near a handoff boundary already is.
/// </para>
///
/// <para>
/// <b>Defaults byte-identical (T230 acceptance):</b> while both settings read false,
/// <see cref="Produce"/> returns immediately — it never even reads the station clock, let alone
/// enqueues. The existing <c>StationIdEveryNUnits</c> unit-count cadence
/// (<see cref="Orchestrator"/>'s own trigger) is completely untouched by this class either way.
/// </para>
/// </summary>
/// <param name="deferralQueue">
/// The SAME <see cref="SpeechDeferralQueue"/> singleton <see cref="Orchestrator"/> drains at track
/// boundaries (SPEC F74.1) — this producer only ever enqueues into it, never drains.
/// </param>
/// <param name="imagingSettings">
/// The live <c>Station:Imaging:*</c> gate (SPEC F110.1/F110.3), read fresh on every
/// <see cref="Produce"/> call so a live operator edit reaches the very next tick with no process
/// restart.
/// </param>
/// <param name="timeProvider">
/// The clock fallback used when <paramref name="stationClock"/> is not supplied — same optional-seam
/// posture <see cref="ScheduleResolver"/>/<see cref="Orchestrator"/> already establish.
/// </param>
/// <param name="stationClock">
/// The live station-local clock (gh-#117, <c>Station:Timezone</c>) when the composition supplies
/// one — "top of hour" means the STATION's wall clock, never the container's (the 6am show at 6am
/// lesson). Optional so a rig that never wires <see cref="IStationClockProvider"/> still constructs
/// and falls back to <paramref name="timeProvider"/>'s own zone, unchanged from every other optional
/// consumer of this seam.
/// </param>
public sealed class ClockAnchoredImagingProducer(
    SpeechDeferralQueue deferralQueue,
    IStationImagingSettingsProvider imagingSettings,
    TimeProvider timeProvider,
    IStationClockProvider? stationClock = null)
{
    const string Reason = "clock-anchored: station-local top of the hour";

    /// <summary>
    /// One tick's worth of work (SPEC F110.1/F110.3): reads the live settings, and — only when at
    /// least one is on — enqueues the gated deferral(s), both due at the SAME next station-local
    /// top-of-hour instant, PROVIDED nothing of that kind is already pending
    /// (<see cref="SpeechDeferralQueue.EnqueueIfAbsent"/>, PLAN T230 review F1 — see this class's own
    /// "Due-ness" remarks for why a plain supersede would erase a still-pending, due-but-unaired
    /// deferral). Idempotent either way: calling this repeatedly before the target hour arrives sees
    /// the same pending entry and no-ops, collapsing to exactly one pending deferral per kind — safe
    /// to call on every ticker interval with no cadence logic of its own.
    /// </summary>
    public void Produce()
    {
        var settings = imagingSettings.Current;
        if (!settings.ClockAnchoredIdents && !settings.TimeAnnouncements)
            return; // T230 acceptance: defaults-false ⇒ not even a clock read, let alone an enqueue.

        var topOfHour = NextStationLocalTopOfHour();

        if (settings.ClockAnchoredIdents)
            deferralQueue.EnqueueIfAbsent(SpeechDeferralKind.StationId, Reason, topOfHour);

        if (settings.TimeAnnouncements)
            deferralQueue.EnqueueIfAbsent(SpeechDeferralKind.TimeDate, Reason, topOfHour);
    }

    /// <summary>
    /// The next station-local top-of-hour as a real instant: the station-local wall clock's current
    /// hour, floored, plus one (a call landing exactly ON a wall-clock hour boundary targets the
    /// FOLLOWING hour, never the one that just started) — a wall-clock target genuinely never in the
    /// past, once resolved through this method's own DST remarks below, which is what keeps every
    /// enqueue genuinely future-dated. Station-local via the live
    /// <see cref="IStationClockProvider"/> seam (<c>Station:Timezone</c>) when the composition
    /// supplies one, otherwise <see cref="timeProvider"/>'s own zone — the same optional-seam
    /// fallback <see cref="ScheduleResolver.Resolve"/> and <see cref="Orchestrator"/>'s own
    /// <c>StationLocalNow</c> already establish.
    ///
    /// <para>
    /// <b>DST:</b> the target wall-clock instant is resolved through <see cref="WallClockInstantResolver.Resolve"/>
    /// — the SAME shared helper <see cref="ScheduleResolver"/>'s own <c>ResolveWallClockInstant</c>
    /// delegates to (PLAN T230 review F2) — spring-forward's missing hour (e.g. 02:00 the morning the
    /// clock jumps 02:00→03:00) resolves FORWARD to the first wall-clock minute that DOES exist, so
    /// that hour simply never gets its own top-of-hour tick; fall-back's repeated hour (e.g. 01:00
    /// occurring twice) resolves to its FIRST occurrence — the offset still in effect before the
    /// clocks roll back — UNLESS that first occurrence has already elapsed relative to "now", in which
    /// case the SECOND occurrence is used instead (PLAN T230 review F3: this producer's own prior
    /// hand-rolled copy omitted that clause, so a &gt;1-hour fall-back zone — e.g. Antarctica/Troll's
    /// 2-hour DST shift — could still be inside the SAME repeated wall-clock window on a LATER tick,
    /// re-arming a past-dated deferral every time). Sharing the resolver makes this true: the target
    /// can never already be in the past.
    /// </para>
    /// </summary>
    DateTimeOffset NextStationLocalTopOfHour()
    {
        var zone = stationClock?.Zone ?? timeProvider.LocalTimeZone;
        var localNow = stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

        var flooredHour = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0, DateTimeKind.Unspecified);
        var nextHour = flooredHour.AddHours(1);

        return WallClockInstantResolver.Resolve(nextHour, zone, localNow);
    }
}
