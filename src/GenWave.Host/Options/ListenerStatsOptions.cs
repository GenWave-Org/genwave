namespace GenWave.Host.Options;

/// <summary>
/// Cadence for the listener-count poller (gh-#10, plugin-readiness P1.4). Bound from the
/// <c>ListenerStats</c> section — boot-time config, deliberately NOT a live station setting: a
/// sampling cadence is an operator/deployment concern, not on-air behavior, and nothing consumes
/// the samples live yet.
/// </summary>
public sealed class ListenerStatsOptions
{
    public const string SectionName = "ListenerStats";

    /// <summary>
    /// Seconds between listener-count samples (default 60). Zero or negative disables the poller
    /// entirely — the hosted service starts, logs that it is disabled, and exits.
    /// </summary>
    public int PollSeconds { get; set; } = 60;
}
