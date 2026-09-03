namespace GenWave.Plugins;

/// <summary>
/// A plugin's outcome after <see cref="PluginLoader"/> has finished with it (SPEC F156.4/F156.7,
/// STORY-385). Two per-CANDIDATE values, deliberately: F156.4's WARN+skip posture rules out any
/// partial or in-between state by construction — a plugin either fully loaded (assembly loaded, entry
/// type activated, <c>Register</c> ran without throwing, every context-provider key passed
/// pre-validation) or it is skipped whole, with zero of its registrations surviving. A third value
/// (T392 review finding 1) covers the one failure that is not about any candidate at all: the plugins
/// ROOT itself could not even be enumerated.
/// </summary>
public enum PluginLoadState
{
    /// <summary>Loaded end to end; its registrations are in <see cref="PluginLoadResult"/>.</summary>
    Loaded,

    /// <summary>Skipped whole — see <see cref="PluginLoadReport.Reason"/> and
    /// <see cref="PluginLoadReport.Detail"/> for why. No registration from this plugin survives.</summary>
    Skipped,

    /// <summary>
    /// <c>PluginManifestDiscovery.EnumerateCandidates</c> itself threw while walking
    /// <c>pluginsRoot</c> — a permission-denied directory or one that vanished mid-walk — before any
    /// candidate was ever identified. <c>PluginLoadResult.Reports</c> carries exactly one report in
    /// this state (never mixed with candidate reports, since no candidate was ever reached) — the
    /// surface that keeps <c>PluginLoader.LoadAll</c>'s own "never throws" promise (T392 review
    /// finding 1) even when the ROOT, not a plugin, is what misbehaved.
    /// </summary>
    RootUnreadable,
}
