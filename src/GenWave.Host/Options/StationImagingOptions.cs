namespace GenWave.Host.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Clock-anchored imaging knobs within the Station config section (SPEC F110.1/F110.3, gh-#381,
/// PLAN T226). This task only adds the binding + allowlist entries (<c>Station:Audience</c>'s own
/// T111 precedent, StationOptions' own remarks) — no consumer reads these yet; PLAN T230's
/// top-of-hour producer, called by the same <c>ContextTickerService</c> this task adds, is the
/// first reader. Bound to <c>Station:Imaging</c>.
/// </summary>
public sealed class StationImagingOptions
{
    /// <summary>SPEC F110.1: each top of hour enqueues a future-dated <c>StationId</c> deferral.
    /// Off by default — the existing <c>StationIdEveryNUnits</c> cadence is untouched and remains
    /// the default sound.</summary>
    public bool ClockAnchoredIdents { get; set; }

    /// <summary>SPEC F110.3: the top-of-hour producer also enqueues a <c>TimeDate</c> deferral. Off
    /// by default.</summary>
    public bool TimeAnnouncements { get; set; }

    /// <summary>
    /// SPEC F124.4/F141.1 (PLAN T269/T326): the live elapsed-due expiry budget, in SECONDS (F141.1's
    /// own unit change — a <c>TimeDate</c> deferral draining more than this far past its own air-time
    /// is dropped undrained rather than airing a stale hour. Defaults to 420 — SPEC F141.1's widened
    /// budget (gh-#526's field data: every real overrun landed 313-362s past Due, just past the
    /// original 300s/5-minute shipped budget — the break just arrives late, so the fix widens the
    /// budget AND, inside it, speaks an honest late variant rather than staying silent; see
    /// <c>GenWave.Orchestration.Orchestrator</c>'s own 90-second honesty-threshold remarks).
    /// StationId (idents) are deliberately exempt; this knob governs <c>TimeDate</c> only.
    ///
    /// <para>
    /// <c>[Range(1, int.MaxValue)]</c> is documentation-only here — <c>ValidateDataAnnotations()</c>
    /// on the root <c>StationOptions</c> in <c>Program.cs</c> does not recurse into this nested class
    /// (the same story every other <c>Station:*</c> nested knob carries, per <see cref="StationOptionsValidator"/>'s
    /// own remarks). <see cref="StationOptionsValidator.Validate"/> is the REAL boot-time floor: a
    /// value below 1 (0 included — 0 would drop EVERY TimeDate deferral, unlike the "0 disables"
    /// convention every other imaging/cadence knob uses) fails boot rather than silently killing
    /// F110.3. <see cref="GenWave.Host.Configuration.SettingValidator"/> enforces the identical [1,
    /// 86400] (1s floor, 1-day ceiling) floor/ceiling on the live-edit path.
    /// </para>
    /// </summary>
    [Range(1, int.MaxValue)]
    public int TimeAnnouncementBudgetSeconds { get; set; } = 420;
}
