namespace GenWave.Host.Options;

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
}
