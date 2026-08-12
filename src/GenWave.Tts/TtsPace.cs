namespace GenWave.Tts;

/// <summary>
/// Classifies and clamps a persona's raw <c>VoiceSpec.Pace</c> before it ever reaches
/// <c>TtsRenderContext.Pace</c> or <see cref="TtsSegmentSource"/>'s own segment cache key (SPEC
/// F98.2, PLAN T140). <see cref="ActivePersonaPaceCache"/> is the ONE call site — every reader of
/// <c>TtsRenderContext.Pace</c> downstream (<see cref="KokoroTtsSynthesizer"/>,
/// <see cref="KokoroFallbackRenderer"/>) trusts the value it carries without re-validating it,
/// exactly the way both adapters already trust an upstream-resolved
/// <c>TtsRenderContext.Rules</c> (SPEC F97.6) — resolved once, at the segment source, never
/// re-checked at the engine.
///
/// <para>
/// <b>Why validate at all:</b> <c>System.Text.Json</c>'s <c>JsonSerializer.Serialize</c> throws on
/// a <c>NaN</c>/<c>Infinity</c> <see langword="double"/> field by default. Reaching an engine
/// adapter's request-body serialization unvalidated, that throw looks like an ordinary hop failure
/// to <see cref="FallbackTtsSynthesizer"/> — and since the primary AND every kokoro-kind fallback
/// hop would read the SAME poisoned context, they would ALL throw the same way, driving
/// <see cref="DependencyHealthStore"/> to mark Kokoro unhealthy and silently, permanently routing
/// EVERY subsequent render (not just this one persona's) through the fallback chain — never a
/// loud, attributable failure. Validating once, here, before the value is ever stamped onto a
/// context, keeps a bad catalog value from ever reaching that call.
/// </para>
///
/// <para>
/// Deliberately pure and stateless — no logger, no WARN of its own. A DEGENERATE value recurs for
/// as long as a persona's card stands uncorrected, and <see cref="ActivePersonaPaceCache"/> polls
/// this on a 30s cadence: logging from in here would re-WARN every single poll forever for one
/// standing-bad card. The WARN — and the once-per-standing-value latch that keeps it from
/// repeating — belongs to the STATEFUL caller, not this pure classifier; see
/// <see cref="ActivePersonaPaceCache"/>'s own remarks.
/// </para>
/// </summary>
static class TtsPace
{
    /// <summary>
    /// kokoro-fastapi's own documented <c>speed</c> window for <c>POST /v1/audio/speech</c> —
    /// roughly [0.5, 2.0]; audio quality degrades audibly outside it. A finite, positive value
    /// outside this range still describes an honest rate ("as slow/fast as the engine allows"), so
    /// it clamps to the nearest bound rather than resetting to <see cref="EngineDefault"/>.
    /// </summary>
    public const double MinSpeed = 0.5;

    /// <summary>See <see cref="MinSpeed"/>.</summary>
    public const double MaxSpeed = 2.0;

    /// <summary>
    /// <c>VoiceSpec.Pace</c>'s own "engine default" sentinel (SPEC F98.1) — also where a
    /// DEGENERATE value (see <see cref="IsDegenerate"/>) lands, since it describes no honest
    /// playback rate at all, and the safest fallback is exactly what an unset <c>Pace</c> already
    /// means.
    /// </summary>
    public const double EngineDefault = 1.0;

    /// <summary>
    /// <c>NaN</c>, <c>Infinity</c>, zero, and negative values are DEGENERATE — none describes a
    /// real playback rate. The caller decides what a degenerate value means for logging (see
    /// <see cref="ActivePersonaPaceCache"/>'s WarnOnce latch); this class only classifies.
    /// </summary>
    public static bool IsDegenerate(double rawPace) =>
        double.IsNaN(rawPace) || double.IsInfinity(rawPace) || rawPace <= 0;

    /// <summary>
    /// Resolves <paramref name="rawPace"/> — a persona card's own <c>VoiceSpec.Pace</c>,
    /// unvalidated operator/import data — to a value safe to serialize and safe to send to Kokoro.
    /// A <see cref="IsDegenerate"/> value resolves to <see cref="EngineDefault"/>, never a render
    /// failure (F98.1's own "pace is simply not applied" posture extended to "an unusable pace is
    /// simply not applied" either). Every other finite value clamps into
    /// [<see cref="MinSpeed"/>, <see cref="MaxSpeed"/>] — an honest rate request, just outside what
    /// the engine supports.
    /// </summary>
    public static double Clamp(double rawPace) =>
        IsDegenerate(rawPace) ? EngineDefault : Math.Clamp(rawPace, MinSpeed, MaxSpeed);
}
