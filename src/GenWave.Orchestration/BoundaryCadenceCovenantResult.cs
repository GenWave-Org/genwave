namespace GenWave.Orchestration;

using System.Globalization;

/// <summary>
/// SPEC F142 (STORY-356, PLAN T327) — the outcome of <see cref="BoundaryCadenceCovenant.Evaluate"/>:
/// the three terms the covenant reasons from, plus the value that actually binds. A satisfying
/// configuration binds <paramref name="ConfiguredLookahead"/> verbatim
/// (<see cref="BoundLookahead"/> equals it, <see cref="WasClamped"/> is <see langword="false"/>); a
/// violating one clamps up to <paramref name="SignOffLeadTime"/> + <paramref name="WorstCasePullGap"/>
/// ceiled to <c>Evaluate</c>'s <c>grain</c> parameter (never down — fail-safe, F142.2) —
/// <see cref="BoundLookahead"/> IS that clamped value, not the raw, pre-grain requirement, so a WARN
/// built from these terms names the clamp that truly binds (T327 review F2).
/// </summary>
public sealed record BoundaryCadenceCovenantResult(
    TimeSpan ConfiguredLookahead,
    TimeSpan SignOffLeadTime,
    TimeSpan WorstCasePullGap,
    TimeSpan BoundLookahead)
{
    /// <summary>Did the configured value fail to cover the covenant and get bound up instead of verbatim?</summary>
    public bool WasClamped => BoundLookahead != ConfiguredLookahead;

    /// <summary>
    /// The one WARN line's message (F142.2 — "names all three values and the clamp applied"), or
    /// <see langword="null"/> on the silent, satisfying-configuration path (F142.3) — also
    /// <see langword="null"/> when <see cref="ConfiguredLookahead"/> is <see cref="TimeSpan.Zero"/>
    /// (the disabled kill switch, never a violation). The Host-side
    /// <c>BoundaryCadenceCovenantPostConfigure</c> logs this verbatim; kept here, beside the terms
    /// it names, rather than reassembled at the log call site.
    /// </summary>
    public string? WarningMessage => WasClamped
        ? $"Station:BoundaryBias:LookaheadMinutes ({Secs(ConfiguredLookahead)}) does not cover " +
          $"SignOffLeadTime ({Secs(SignOffLeadTime)}) plus the worst-case feeder pull gap " +
          $"({Secs(WorstCasePullGap)}) — clamped up to {Secs(BoundLookahead)} (SPEC F142)."
        : null;

    static string Secs(TimeSpan value) => $"{value.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)}s";
}
