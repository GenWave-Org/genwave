using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Deployment-tuning knob for <c>ContextTickerService</c>'s own polling cadence — NOT part of the
/// operator-editable settings allowlist, the same env/compose-only posture
/// <see cref="ListenerStatsOptions.PollSeconds"/>/<see cref="DependencyHealthOptions"/> already
/// carry: this governs how often the ticker CALLS IN, never a provider's own fetch cadence
/// (<c>Context:{Key}:SegmentCadenceMinutes</c>, which stays live via
/// <see cref="ConfigurationContextSettingsProvider"/> regardless of this value —
/// <c>GenWave.Context.ContextPipeline</c> itself is what rate-limits a fetch to once per cadence
/// slot; this interval only bounds how promptly a newly-due segment reaches the deferral queue once
/// its slot opens). Bound to <c>ContextTicker</c>, a top-level section deliberately NOT nested under
/// <c>Context</c> — that prefix is reserved for <c>Context:{Key}:*</c> provider settings
/// (<see cref="ConfigurationContextSettingsProvider"/>'s own remarks), and this is not one.
/// </summary>
public sealed class ContextTickerOptions
{
    public const string Section = "ContextTicker";

    /// <summary>Seconds between ticks (default 30). Floored at 1 — unlike
    /// <see cref="ListenerStatsOptions.PollSeconds"/>, there is no "disabled ticker" state: even
    /// with zero providers enabled, the ticker keeps calling in cheaply so a live PUT that enables
    /// one takes effect on the very next tick, not after a restart.</summary>
    [Range(1, int.MaxValue)]
    public int TickIntervalSeconds { get; set; } = 30;
}
