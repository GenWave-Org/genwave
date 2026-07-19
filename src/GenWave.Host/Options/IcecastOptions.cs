namespace GenWave.Host.Options;

/// <summary>
/// Wiring for <see cref="Stats.IcecastListenerStatsSource"/>'s admin-stats poll (SPEC F62.12
/// addendum, STORY-179, gitea-#10). Bound from the <c>Icecast</c> config section —
/// env/compose-only, like <see cref="SpectatorOptions.PublicPort"/> and <see cref="AdminOptions.Enabled"/>:
/// deliberately absent from <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>.
/// <see cref="AdminPassword"/> in particular must NEVER become allowlist-readable (SPEC F19.3) —
/// the same secret-exclusion rule as <c>Admin:Password</c>/<c>ConnectionStrings:*</c>.
/// </summary>
public sealed class IcecastOptions
{
    public const string SectionName = "Icecast";

    /// <summary>
    /// Icecast's base URL (e.g. <c>http://icecast:8000</c>), or empty to disable the listener-count
    /// poll entirely — <see cref="Stats.IcecastListenerStatsSource"/> degrades to a null count
    /// rather than throwing when this is blank.
    /// </summary>
    public string StatsUrl { get; init; } = "";

    /// <summary>The Icecast admin user's password (username is always <c>admin</c>, per icecast.xml.tmpl).</summary>
    public string AdminPassword { get; init; } = "";
}
