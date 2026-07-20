using System.ComponentModel.DataAnnotations;

namespace GenWave.Tts;

/// <summary>
/// Thresholds for the auto drop/raise state machine (SPEC F69.2, STORY-188). Deployment-tunable,
/// not operator-facing — unlike <see cref="LlmOptions.DegradationPin"/> this section carries no
/// station-settings allowlist entry (a station rarely needs to retune how twitchy its own
/// degradation ladder is; the pin is the lever an operator actually reaches for).
/// <see cref="DegradationController"/> reads both fresh via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so an env/appsettings edit
/// applies without a code change, per SPEC F69.2's "thresholds are options, not code".
/// </summary>
public sealed class DegradationOptions
{
    public const string Section = "Degradation";

    /// <summary>
    /// Consecutive real LLM call failures (SPEC F69.2) — sourced from
    /// <see cref="LlmCopyStatusHolder.ConsecutiveFailureCount"/>, genuine on-air attempts only,
    /// never a probe — that drop the mode one step.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ConsecutiveFailureThreshold { get; set; } = 3;

    /// <summary>
    /// Minimum time the current mode must have held before a cached probe verdict (SPEC F69.2) is
    /// allowed to move it one step — healthy raises, unhealthy drops — guarding against flapping on
    /// a probe that briefly recovers (or fails) and reverses again. One value governs both
    /// directions deliberately (T32 review finding: the drop and raise probe checks are meant to be
    /// symmetric, not two independently-tunable knobs) — see
    /// <see cref="DegradationController.TryDropByProbe"/>/<see cref="DegradationController.TryRaise"/>.
    /// <para>
    /// Also reused, not a separate option, as the width of <see cref="DegradationGatedCopyWriter"/>'s
    /// Soft cadence window: one real LLM attempt is permitted per window of this length while in
    /// Soft (SPEC F69.1 "minimized calls"). One knob controls both "how twitchy is auto drop/raise"
    /// and "how many real Soft attempts happen per unit time" — deliberately, since both answer the
    /// same underlying question (how long before treating a signal as durable rather than transient).
    /// </para>
    /// </summary>
    [Range(1, int.MaxValue)]
    public int CooldownSeconds { get; set; } = 60;
}
